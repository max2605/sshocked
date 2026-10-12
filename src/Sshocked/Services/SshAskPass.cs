namespace Sshocked.Services;

public static class SshAskPass
{
    public const string HelperEnvironment = "SSHOCKED_INTERNAL_ASKPASS";
    public const string PasswordEnvironment = "SSHOCKED_ASKPASS_PASSWORD";
    private const string ExecutableEnvironment = "SSH_ASKPASS";
    private const string RequirementEnvironment = "SSH_ASKPASS_REQUIRE";
    private const string DisplayEnvironment = "DISPLAY";

    public static void Configure(IDictionary<string, string?> variables, string password)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Password secrets require the sshocked executable/apphost. Launch ssk directly instead of dotnet ssk.dll.");
        variables[ExecutableEnvironment] = executable;
        variables[RequirementEnvironment] = "force";
        variables[DisplayEnvironment] = Environment.GetEnvironmentVariable(DisplayEnvironment) ?? "sshocked:0";
        variables[HelperEnvironment] = "1";
        variables[PasswordEnvironment] = password;
    }
}
