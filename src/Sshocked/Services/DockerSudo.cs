using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

/// <summary>Feeds sudo credentials through protected SSH stdin, never through remote command text.</summary>
internal static class DockerSudo
{
    public static async Task<ProcessResult> RunResult(IProcessService processes, ISecretProviderRegistry secrets, ServerHost host, string command, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var password = host.SudoPassword;
        if (host.SudoPasswordSecretRef is { } reference)
        {
            if (reference.SecretType != SecretType.Password) throw new ArgumentException("Sudo password reference must use Password.");
            var secret = await secrets.Resolve(reference, ct);
            if (!secret.Success || secret.Value is null) throw new InvalidOperationException(secret.ErrorMessage ?? "Could not resolve sudo password.");
            ct.ThrowIfCancellationRequested();
            password = secret.Value;
        }
        ProcessResult result;
        if (string.IsNullOrEmpty(password))
        {
            result = await processes.RunSshCommandResultNonInteractive(host, command, ct);
            if (result.ExitCode != 0 && result.ExitCode != 255 && result.StandardError.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
                && (result.StandardError.Contains("docker.sock", StringComparison.OrdinalIgnoreCase) || result.StandardError.Contains("connect", StringComparison.OrdinalIgnoreCase)))
                result = await processes.RunSshCommandResultNonInteractive(host, "sudo -n " + command, ct);
        }
        else result = await processes.RunSshCommandWithInputNonInteractive(host, "sudo -S -p '' " + command, password + "\n", ct);
        string Redact(string text)
        {
            text = SshCredentialRedactor.RedactCommand(text, host);
            return string.IsNullOrEmpty(password) ? text : text.Replace(password, "[REDACTED]", StringComparison.Ordinal);
        }
        return result with { StandardOutput = Redact(result.StandardOutput), StandardError = Redact(result.StandardError) };
    }
}
