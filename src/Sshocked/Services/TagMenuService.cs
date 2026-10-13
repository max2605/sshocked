using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class TagMenuService(IConfigRepository repository, IServerTagService tags,
    ITableRendererService tables, IMenuFactory menus, IUiChromeService chrome,
    IHostSelectorService hostSelector, IServerMenuService servers, IGroupSessionService sessions,
    FileTransferUi transfers, IMacroMenuService macros) : ITagMenuService
{
    public async Task Show()
    {
        while (true)
        {
            var config = repository.Load();
            var available = tags.GetAll(config.Hosts);
            var palette = UiThemePalette.For(config.UI.Theme);
            AnsiConsole.Clear();
            chrome.RenderBreadcrumb("Main", "Servers", "Tags");
            AnsiConsole.MarkupLine($"[{palette.Muted}]Tags select current servers across static groups. Edit a server to assign tags.[/]");
            if (available.Count == 0) AnsiConsole.MarkupLine($"[{palette.Muted}]No server tags assigned yet.[/]");
            var entries = available.Select(tag => new MenuEntry($"{Markup.Escape(tag)} [grey]({tags.Filter(config.Hosts, [tag]).Count} servers)[/]",
                executeAsync: () => ShowTargets([tag]))).ToList();
            if (available.Count > 1)
                entries.Add(new("Filter by multiple tags", 'T', executeAsync: () => SelectTags(available, config)));
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menus.RunMenu("[bold yellow]Filter Servers by Tag[/]", entries)) return;
        }
    }

    private Task SelectTags(List<string> available, AppConfig config)
    {
        var selected = AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
            .Title("Select tags ([grey]space selects, Enter applies; none cancels[/]):")
            .NotRequired().InstructionsText("[grey]Matches all selected tags. Space selects; Enter applies.[/]")
            .UseConverter(Markup.Escape).AddChoices(available)
            .HighlightStyle(UiThemePalette.For(config.UI.Theme).SelectionStyle));
        return selected.Count == 0 ? Task.CompletedTask : ShowTargets(selected);
    }

    private async Task ShowTargets(List<string> selectedTags)
    {
        var label = string.Join(" + ", selectedTags);
        while (true)
        {
            var config = repository.Load();
            var hosts = tags.Filter(config.Hosts, selectedTags);
            AnsiConsole.Clear();
            chrome.RenderBreadcrumb("Main", "Servers", "Tags", label);
            tables.RenderGroup("Tags: " + label, hosts);
            AnsiConsole.MarkupLine($"[{UiThemePalette.For(config.UI.Theme).Muted}]{hosts.Count} servers match all selected tags. Membership refreshes after each action.[/]");
            var entries = new List<MenuEntry>();
            if (hosts.Count > 0)
            {
                entries.Add(new("Select server...", 'S', executeAsync: () => SelectServer(hosts)));
                entries.Add(new("Connect all matching servers", 'C', executeAsync: () => sessions.ConnectServers(config, hosts)));
                entries.Add(new("Run command on matching servers", 'R', executeAsync: () => sessions.RunServerCommands(hosts)));
                entries.Add(new("Run macro on matching servers", 'M', executeAsync: () => macros.Run(hosts)));
                entries.Add(new("Upload to matching servers", 'U', executeAsync: () => transfers.UploadGroup(hosts)));
                entries.Add(new("Download from matching servers", 'O', executeAsync: () => transfers.DownloadGroup(hosts)));
            }
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menus.RunMenu("[bold yellow]Tag Actions[/]", entries)) return;
        }
    }

    private Task SelectServer(List<ServerHost> hosts)
    {
        var host = hostSelector.SelectHost(hosts, "Select a [green]server[/]");
        return host is null ? Task.CompletedTask : servers.ShowServerActions(host);
    }
}
