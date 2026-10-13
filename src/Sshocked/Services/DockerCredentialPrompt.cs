using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class DockerCredentialPrompt
{
    public static bool Prepare(ServerHost host, ILocalCredentialService? local)
    {
        if (host.AuthType != AuthType.Password || host.PasswordSecretRef is not null || !string.IsNullOrEmpty(host.SshPassword) || local?.HasPassword(host) == true) return true;
        var password = AnsiConsole.Prompt(new TextPrompt<string>($"SSH password for [cyan]{Markup.Escape(host.Alias)}[/] ([grey]empty cancels[/]):").Secret().AllowEmpty());
        if (password.Length == 0) return false;
        host.SshPassword = password;
        return true;
    }
}
