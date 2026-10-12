using System.Runtime.Versioning;
using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SecretKeyFile : IAsyncDisposable
{
    private readonly FileStream? stream;
    private readonly string directory;
    private static readonly ConcurrentDictionary<string, string> pendingWindowsKeys = new(StringComparer.OrdinalIgnoreCase);
    public string Path { get; }
    static SecretKeyFile()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var key in pendingWindowsKeys)
            {
                try { File.Delete(key.Key); Directory.Delete(key.Value); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        };
    }
    private SecretKeyFile(string directory, string path, FileStream? stream) { this.directory = directory; Path = path; this.stream = stream; }

    public static async Task<SecretKeyFile> Create(string value, SecretType type, CancellationToken ct = default)
    {
        if (type == SecretType.Certificate)
        {
            var bytes = Convert.FromBase64String(value);
            try
            {
                using var certificate = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
                using var rsa = certificate.GetRSAPrivateKey();
                using var ecdsa = certificate.GetECDsaPrivateKey();
                value = rsa?.ExportPkcs8PrivateKeyPem() ?? ecdsa?.ExportPkcs8PrivateKeyPem() ?? throw new InvalidOperationException("Certificate has no exportable RSA/ECDSA private key.");
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        if (!value.Contains("PRIVATE KEY-----", StringComparison.Ordinal)) throw new InvalidOperationException("Key Vault secret must contain a PEM/OpenSSH private key (or use Certificate for an exportable PKCS#12 secret).");
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sshocked-key-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) CreatePrivateWindowsDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = System.IO.Path.Combine(directory, "identity");
        FileStream? stream = null;
        try
        {
            // Windows OpenSSH readers do not necessarily request FILE_SHARE_DELETE.
            // Keep DeleteOnClose on Unix; close Windows writer before SSH reads, and
            // delete explicitly after the session (also on normal application exit).
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.Read | FileShare.Delete,
                Options = FileOptions.Asynchronous | (OperatingSystem.IsWindows() ? FileOptions.None : FileOptions.DeleteOnClose)
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            stream = new FileStream(path, options);
            var bytes = Encoding.UTF8.GetBytes(value.TrimEnd() + "\n");
            try { await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            if (OperatingSystem.IsWindows())
            {
                pendingWindowsKeys[path] = directory;
                await stream.DisposeAsync();
                stream = null;
            }
            return new SecretKeyFile(directory, path, stream);
        }
        catch
        {
            if (stream != null) await stream.DisposeAsync();
            Directory.Delete(directory, recursive: true);
            pendingWindowsKeys.TryRemove(path, out _);
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreatePrivateWindowsDirectory(string path)
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Cannot determine current Windows identity.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(security);
    }

    public async ValueTask DisposeAsync()
    {
        if (stream != null) await stream.DisposeAsync();
        if (File.Exists(Path)) File.Delete(Path);
        if (Directory.Exists(directory)) Directory.Delete(directory);
        pendingWindowsKeys.TryRemove(Path, out _);
    }
}
