using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

/// <summary>Feeds sudo credentials through protected SSH stdin, never through remote command text.</summary>
internal static class DockerSudo
{
    public static async Task<string> Run(IProcessService processes, ISecretProviderRegistry secrets, ServerHost host, string command)
    {
        var password = host.SudoPassword;
        if (host.SudoPasswordSecretRef is { } reference)
        {
            if (reference.SecretType != SecretType.Password) throw new ArgumentException("Sudo password reference must use Password.");
            var secret = await secrets.Resolve(reference);
            if (!secret.Success || secret.Value is null) throw new InvalidOperationException(secret.ErrorMessage ?? "Could not resolve sudo password.");
            password = secret.Value;
        }
        if (string.IsNullOrEmpty(password)) return await processes.RunSshCommand(host, "sudo -n " + command);
        var result = await processes.RunSshCommandWithInput(host, "sudo -S -p '' " + command, password + "\n");
        result.EnsureSuccess("Docker command");
        return result.StandardOutput + result.StandardError;
    }
}
