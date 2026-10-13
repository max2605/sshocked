using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class QuickAccessMenuService(IRecentConnectionService recents, IMenuFactory menus,
    IServerMenuService servers, IContainerMenuService containers, IUiChromeService chrome) : IQuickAccessMenuService
{
    public Task ShowFavorites() => Show(false);
    public Task ShowRecent() => Show(true);

    private async Task Show(bool recent)
    {
        while (true)
        {
            AnsiConsole.Clear();
            chrome.RenderBreadcrumb("Main", recent ? "Recent connections" : "Favorites");
            var targets = recent ? recents.Recent() : recents.Favorites();
            var entries = targets.Select(t => new MenuEntry(Markup.Escape(t.Label) +
                (t.LastConnectedAt.HasValue ? $" [grey]{t.LastConnectedAt.Value.ToLocalTime():g}[/]" : ""),
                executeAsync: () => OpenTarget(t))).ToList();
            if (entries.Count == 0) AnsiConsole.MarkupLine("[grey]No entries yet.[/]");
            if (recent) entries.Add(new("Clear recents", 'L', executeAsync: () =>
            {
                if (AnsiConsole.Confirm("Clear recent connections?", false)) recents.Clear();
                return Task.CompletedTask;
            }));
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menus.RunMenu(recent ? "[yellow]Recent connections[/]" : "[yellow]Favorites[/]", entries)) return;
        }
    }

    private Task OpenTarget(ConnectionTarget target) => target.Container is null
        ? servers.ShowServerActions(target.Host)
        : containers.ShowSavedActions(target.Host, target.Container);
}
