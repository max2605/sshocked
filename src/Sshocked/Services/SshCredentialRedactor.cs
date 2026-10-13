using Sshocked.Models;

namespace Sshocked.Services;

public static class SshCredentialRedactor
{
    public static string RedactCommand(string command, ServerHost preparedHost)
    {
        // Resolved credentials belong to the ephemeral lease; configuration hosts never hold saved passwords.
        foreach (var password in new[] { preparedHost.SudoPassword, preparedHost.SshPassword })
            if (!string.IsNullOrEmpty(password)) command = command.Replace(password, SshConnectionConstants.Redacted, StringComparison.Ordinal);
        return command;
    }
}
