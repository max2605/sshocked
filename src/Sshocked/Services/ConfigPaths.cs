namespace Sshocked.Services;

public static class ConfigPaths
{
    public static string DirectoryPath => Environment.GetEnvironmentVariable("SSHOCKED_CONFIG_DIR") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "sshocked");
}
