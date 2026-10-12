using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SettingsMenuService(IConfigRepository configRepository, IMenuFactory menuFactory,
    ISecretConfigurationUi secretUi, SessionRecordingConfigurationUi recordingUi,
    IUiSettingsMenuService uiSettings, IConsoleHelperService consoleHelper) : ISettingsMenuService
{
    public async Task Show()
    {
        var running = true;
        while (running)
        {
            var config = configRepository.Load();
            AnsiConsole.Clear();
            var currentStrategy = config.DefaultConnectionStrategy == ConnectionStrategy.MultiTab ? "Multi-Tab" : "Sequential";
            AnsiConsole.MarkupLine("[bold yellow]Settings[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  Default connection strategy: [green]{currentStrategy}[/]");
            AnsiConsole.WriteLine();

            var entries = new List<MenuEntry>
            {
                new("Display settings", 'V', executeAsync: uiSettings.Show),
                new("Manage secret providers", 'P', executeAsync: secretUi.Show),
                new("Session recording", 'O', executeAsync: recordingUi.Show),
                new("Global connection hooks", 'K', executeAsync: () =>
                {
                    var hooks = HookConfigurationUi.ConfigureGlobal(config.GlobalHooks);
                    configRepository.Update(current => current.GlobalHooks = hooks);
                    return Task.CompletedTask;
                }),
                new("Toggle recent connection tracking", 'H', executeAsync: () =>
                {
                    configRepository.Update(current => current.TrackRecentConnections = !current.TrackRecentConnections);
                    return Task.CompletedTask;
                }),
                new("Default to Multi-Tab (parallel)", 'M', executeAsync: () => SetStrategy(ConnectionStrategy.MultiTab)),
                new("Default to Sequential (one after another)", 'Q', executeAsync: () => SetStrategy(ConnectionStrategy.Sequential)),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            running = await menuFactory.RunMenu("[bold yellow]Settings[/]", entries);
        }
    }

    private Task SetStrategy(ConnectionStrategy strategy)
    {
        configRepository.Update(config => config.DefaultConnectionStrategy = strategy);
        var label = strategy == ConnectionStrategy.MultiTab ? "Multi-Tab" : "Sequential";
        AnsiConsole.MarkupLine($"[green]✓ Default strategy set to {label}[/]");
        consoleHelper.WaitForKey();
        return Task.CompletedTask;
    }
}
