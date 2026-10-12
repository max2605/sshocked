using Spectre.Console;
using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class SshKeyPickerService(ISshKeyDiscoveryService discovery, IConsoleWriterService console) : ISshKeyPickerService
{
    public string? Pick(string? currentPath = null)
    {
        var keys = discovery.ListAvailableKeys();
        console.WriteLine(discovery.GetAgentStatus().Message);
        console.WriteLine("Encrypted keys still need ssh-agent or a passphrase at connection time. Passphrases are never stored.");
        var choices = new List<string> { SshKeyPickerLabels.UseDefaults, SshKeyPickerLabels.EnterManually };
        if (keys.Count > 0) choices.Insert(0, SshKeyPickerLabels.PickAvailable);
        if (!string.IsNullOrWhiteSpace(currentPath)) choices.Insert(0, SshKeyPickerLabels.KeepCurrent);
        var choice = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[green]SSH identity[/]:").AddChoices(choices));
        if (choice == SshKeyPickerLabels.KeepCurrent) return currentPath;
        if (choice == SshKeyPickerLabels.UseDefaults) return null;
        if (choice == SshKeyPickerLabels.EnterManually)
        {
            var path = AnsiConsole.Prompt(new TextPrompt<string>("[green]Identity file path[/] (empty = default):")
                .DefaultValue(currentPath ?? string.Empty).AllowEmpty());
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        }
        var selected = AnsiConsole.Prompt(new SelectionPrompt<Models.SshKeyInfo>().Title("Select an [green]SSH key[/]:")
            .PageSize(10).EnableSearch().UseConverter(key => Markup.Escape(
                $"{key.Path} | {key.Algorithm} | {key.Fingerprint} | {key.Comment}{(key.IsAgentLoaded ? " | agent loaded" : "")}"))
            .AddChoices(keys));
        return selected.Path;
    }

    public void ShowKeys()
    {
        console.WriteLine(discovery.GetAgentStatus().Message);
        var keys = discovery.ListAvailableKeys();
        if (keys.Count == 0) console.WriteLine("No private keys with readable public-key companions were found.");
        foreach (var key in keys)
            console.WriteLine($"{key.Path}\n  {key.Algorithm} {key.Fingerprint} {key.Comment} {(key.IsAgentLoaded ? "[agent loaded]" : "")}");
    }
}
