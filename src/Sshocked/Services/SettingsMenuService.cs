using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SettingsMenuService(IConfigRepository configRepository, IMenuFactory menuFactory,
    ISecretConfigurationUi secretUi, IActivityMenuService activity,
    IUiSettingsMenuService uiSettings, IConsoleHelperService consoleHelper, ILocalCredentialService localCredentials,
    IUiChromeService chrome, IPathInputService paths) : ISettingsMenuService
{
    public async Task Show()
    {
        var running = true;
        while (running)
        {
            var config = configRepository.Load();
            AnsiConsole.Clear();
            chrome.RenderBreadcrumb("Main", "Settings");
            var palette = UiThemePalette.For(config.UI.Theme);
            AnsiConsole.MarkupLine($"[bold {palette.Accent}]Settings[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  Group connections: [{palette.Success}]parallel multi-tab[/]");
            AnsiConsole.WriteLine();

            var entries = new List<MenuEntry>
            {
                new("Display settings", 'V', executeAsync: uiSettings.Show),
                new("Manage secret providers", 'P', executeAsync: secretUi.Show),
                new("Activity and recording", 'O', executeAsync: activity.Show),
                new("Global connection hooks", 'K', executeAsync: () =>
                {
                    var hooks = HookConfigurationUi.ConfigureGlobal(config.GlobalHooks, paths);
                    configRepository.Update(current => current.GlobalHooks = hooks);
                    return Task.CompletedTask;
                }),
                new("Toggle recent connection tracking", 'H', executeAsync: () =>
                {
                    configRepository.Update(current => current.TrackRecentConnections = !current.TrackRecentConnections);
                    return Task.CompletedTask;
                }),
                new("Clear all saved local passwords", 'L', executeAsync: ClearLocalPasswords),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            running = await menuFactory.RunMenu("[bold yellow]Settings[/]", entries);
        }
    }

    private Task ClearLocalPasswords()
    {
        if (AnsiConsole.Confirm("Clear all locally saved SSH passwords?", false))
        {
            localCredentials.ClearAll();
            AnsiConsole.MarkupLine("[green]Saved local passwords cleared.[/]");
            consoleHelper.WaitForKey();
        }
        return Task.CompletedTask;
    }
}
