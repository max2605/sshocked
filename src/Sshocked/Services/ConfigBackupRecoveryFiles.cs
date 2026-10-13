using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Sshocked.Services;

internal static class ConfigBackupRecoveryFiles
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static string Binding()
    {
        var user = OperatingSystem.IsWindows() ? WindowsUser() : Environment.UserName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user + "\n" + Environment.MachineName)));
    }

    public static string PathFor(string root, string? requested = null)
    {
        root = Path.GetFullPath(root);
        requested ??= "recovery-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json";
        var path = Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(root, requested));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(path), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), comparison)
            || Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Local recovery files must be direct children of the private recovery directory.");
        return path;
    }

    public static void Write(string root, string path, string json)
    {
        path = PathFor(root, path);
        var directory = new DirectoryInfo(Path.GetFullPath(root));
        if (directory.Exists) RejectLink(directory.FullName);
        Directory.CreateDirectory(directory.FullName);
        if (OperatingSystem.IsWindows()) ProtectWindowsDirectory(directory);
        else File.SetUnixFileMode(directory.FullName, DirectoryMode);
        ConfigBackupFiles.Write(path, json);
        if (OperatingSystem.IsWindows()) ProtectWindowsFile(new FileInfo(path));
        else File.SetUnixFileMode(path, FileMode);
    }

    public static string Read(string root, string path)
    {
        path = PathFor(root, path);
        RejectLink(Path.GetFullPath(root));
        RejectLink(path);
        if (OperatingSystem.IsWindows()) ValidateWindowsRights(Path.GetFullPath(root), path);
        else if (File.GetUnixFileMode(Path.GetFullPath(root)) != DirectoryMode || File.GetUnixFileMode(path) != FileMode)
            throw new UnauthorizedAccessException("Local recovery requires a private directory (0700) and file (0600).");
        return ConfigBackupFiles.Read(path);
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Local recovery cannot use symbolic links or reparse points.");
    }

    [SupportedOSPlatform("windows")]
    private static string WindowsUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("Cannot determine the Windows account for local recovery.");
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindowsDirectory(DirectoryInfo directory)
    {
        var user = new SecurityIdentifier(WindowsUser());
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindowsFile(FileInfo file)
    {
        var user = new SecurityIdentifier(WindowsUser());
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        file.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateWindowsRights(string directory, string path)
    {
        var user = new SecurityIdentifier(WindowsUser());
        foreach (var security in new FileSystemSecurity[] { new DirectoryInfo(directory).GetAccessControl(), new FileInfo(path).GetAccessControl() })
        {
            if (!security.AreAccessRulesProtected || !user.Equals(security.GetOwner(typeof(SecurityIdentifier))))
                throw new UnauthorizedAccessException("Local recovery must be owned by and protected for the current Windows account.");
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && !user.Equals(rule.IdentityReference))
                    throw new UnauthorizedAccessException("Local recovery permits access by another Windows account.");
        }
    }
}
