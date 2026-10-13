using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class LocalCredentialUi(ILocalCredentialService credentials)
{
    public void Configure(ServerHost host)
    {
        if (host.AuthType != AuthType.Password && host.AzureBastion?.AuthType != BastionAuthType.Password) return;
        if (!credentials.IsSupported)
        {
            AnsiConsole.MarkupLine("[grey]Local password storage requires Windows. Configure a secret provider for saved passwords on this platform.[/]");
            return;
        }
        var saved = credentials.HasPassword(host);
        var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title($"[green]Local SSH password[/] ({(saved ? "saved" : "not saved")}):")
            .AddChoices("Keep current password", "Save / update password", "Clear saved password"));
        if (choice == "Clear saved password") credentials.ClearPassword(host);
        else if (choice == "Save / update password")
        {
            var password = AnsiConsole.Prompt(new TextPrompt<string>("[green]SSH password[/]:").Secret());
            credentials.SavePassword(host, password);
            AnsiConsole.MarkupLine("[green]Password saved using Windows user encryption.[/]");
        }
    }
}
