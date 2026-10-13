using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class HostSelectorService(IConfigRepository repository, IConsoleWriterService console) : IHostSelectorService
{

    public ServerHost? SelectHost(List<ServerHost> hosts, string title)
    {
        if (hosts.Count == 0) return null;
        var ui = repository.Load().UI;
        var choices = hosts.OrderByDescending(host => host.IsFavorite).ThenBy(host => host.Alias, StringComparer.OrdinalIgnoreCase)
            .Select(host => new HostSelectorServiceChoice(host, $"{HostVisuals.Alias(host, ui.Theme)} ({Markup.Escape(host.HostName)}) {HostVisuals.AuthBadge(host, ui.Theme)} {HostVisuals.DockerBadge(host, ui.Theme)}")).ToList();
        choices.Add(new(null, MenuLabels.Back));
        console.WriteLine("↑/↓ Select  ·  Type to search  ·  Enter Confirm  ·  Clear search to select Back");
        var selected = AnsiConsole.Prompt(new SelectionPrompt<HostSelectorServiceChoice>().Title($"{title} ([grey]clear search for Back[/]):")
            .PageSize(ui.CompactMode ? 15 : 10).EnableSearch().UseConverter(choice => choice.Label).AddChoices(choices)
            .HighlightStyle(UiThemePalette.For(ui.Theme).SelectionStyle));
        return selected.Host;
    }
}
