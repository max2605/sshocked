using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class QuickAccessMenuService(IRecentConnectionService recents, IConsoleHelperService console, IMenuFactory menus) : IQuickAccessMenuService
{
    public Task ShowFavorites() => Show(false);
    public Task ShowRecent() => Show(true);

    private async Task Show(bool recent)
    {
        while (true)
        {
            var targets = recent ? recents.Recent() : recents.Favorites();
            var entries = targets.Select(t => new MenuEntry(Markup.Escape(t.Label) +
                (t.LastConnectedAt.HasValue ? $" [grey]{t.LastConnectedAt.Value.ToLocalTime():g}[/]" : ""),
                executeAsync: () => OpenTarget(t, recent))).ToList();
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

    private async Task OpenTarget(ConnectionTarget target, bool recent)
    {
        var entries = new List<MenuEntry>
        {
            new("Connect", 'C', executeAsync: () => target.Container is null
                ? console.RunSsh(target.Host) : console.RunDockerExec(target.Host, target.Container)),
            new("Toggle favorite", 'F', executeAsync: () => { recents.ToggleFavorite(target.Host, target.Container); return Task.CompletedTask; }),
            new("Back", 'B', actionType: MenuActionType.Back)
        };
        if (recent) entries.Insert(2, new("Remove from recents", 'R', executeAsync: () =>
        {
            recents.Remove(target.Host.Id, target.Container?.Id ?? target.Container?.ContainerId);
            return Task.CompletedTask;
        }));
        await menus.RunMenu(Markup.Escape(target.Label), entries);
    }
}
