using Spectre.Console;
using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class UiSettingsMenuService(IConfigRepository repository, IConsoleWriterService console,
    IUiChromeService chrome) : IUiSettingsMenuService
{
    public Task Show()
    {
        var ui = repository.Load().UI;
        ui.Theme = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Display theme:").AddChoices("Dark", "Light")
            .DefaultValue(ui.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark"));
        repository.Update(config => config.UI.Theme = ui.Theme);
        chrome.ApplyTheme();
        console.WriteLine($"{ui.Theme} theme applied.");
        ui.CompactMode = AnsiConsole.Confirm("Use compact tables (container rows hidden)?", ui.CompactMode);
        ui.ShowReachabilityStatus = AnsiConsole.Confirm("Show host status badges?", ui.ShowReachabilityStatus);
        ui.EnableReachabilityChecks = AnsiConsole.Confirm("Enable background TCP reachability checks?", ui.EnableReachabilityChecks);
        ui.ShowLastConnectedColumn = AnsiConsole.Confirm("Show last connected times?", ui.ShowLastConnectedColumn);
        ui.ShowBreadcrumbs = AnsiConsole.Confirm("Show navigation breadcrumbs?", ui.ShowBreadcrumbs);
        ui.ShowFooter = AnsiConsole.Confirm("Show version/config/log footer?", ui.ShowFooter);
        repository.Update(config => config.UI = ui);
        console.WriteLine("Display settings saved. TCP status describes reachability, not SSH authentication.");
        return Task.CompletedTask;
    }
}
