using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

/// <summary>Stores only DPAPI ciphertext, bound to the Windows user and the SSH endpoint.</summary>
public sealed class LocalCredentialService(string? storageDirectory = null) : ILocalCredentialService
{
    private readonly string directory = Path.GetFullPath(storageDirectory ?? Path.Combine(ConfigPaths.DirectoryPath, "credentials"));
    public bool IsSupported => OperatingSystem.IsWindows();

    public bool HasPassword(ServerHost host) => IsSupported && File.Exists(CredentialPath(host));

    public void SavePassword(ServerHost host, string password)
    {
        if (!OperatingSystem.IsWindows()) throw Unsupported();
        ArgumentException.ThrowIfNullOrEmpty(password);
        var path = CredentialPath(host);
        EnsurePrivateDirectory();
        using var fileLock = CrossProcessFileLock.Acquire(path);
        var plain = Encoding.UTF8.GetBytes(password);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(plain, EndpointEntropy(host), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public string? GetPassword(ServerHost host)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var path = CredentialPath(host);
        if (!Directory.Exists(directory)) return null;
        using var fileLock = CrossProcessFileLock.Acquire(path);
        if (!File.Exists(path)) return null;
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(File.ReadAllBytes(path), EndpointEntropy(host), DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException error)
        {
            throw new InvalidOperationException("The saved password belongs to another Windows account or SSH endpoint. Update or clear it in the server profile.", error);
        }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }

    public void ClearPassword(ServerHost host)
    {
        var path = CredentialPath(host);
        if (!Directory.Exists(directory)) return;
        using var fileLock = CrossProcessFileLock.Acquire(path);
        File.Delete(path);
    }

    public void ClearAll()
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.dpapi"))
        {
            using var fileLock = CrossProcessFileLock.Acquire(path);
            File.Delete(path);
        }
    }

    private string CredentialPath(ServerHost host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host.Id);
        // Host IDs are external configuration data and must never become filesystem paths.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host.Id)));
        return Path.Combine(directory, key + ".dpapi");
    }

    private static byte[] EndpointEntropy(ServerHost host)
    {
        // Editing a hostname/user must not silently send the old server's password to a new endpoint.
        var endpoint = string.Join('\n', "sshocked/password/v1", host.Id, host.HostName.ToLowerInvariant(),
            host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), host.User,
            host.AzureBastion?.TargetResourceId, host.AzureBastion?.TargetIpAddress, host.AzureBastion?.Username,
            host.AzureBastion?.BastionName, host.AzureBastion?.BastionResourceGroup,
            host.AzureBastion?.SubscriptionId, host.AzureBastion?.TenantId,
            host.AzureBastion?.TargetPort.ToString(System.Globalization.CultureInfo.InvariantCulture), host.ProxyJump);
        return SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));
    }

    [SupportedOSPlatform("windows")]
    private void EnsurePrivateDirectory()
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Cannot determine current Windows identity.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var info = new DirectoryInfo(directory);
        if (!info.Exists) info.Create(security);
        else info.SetAccessControl(security);
    }

    private static PlatformNotSupportedException Unsupported() => new("Local password storage requires Windows DPAPI. Use a configured secret provider on this platform.");
}
