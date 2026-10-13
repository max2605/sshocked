using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class TagMenuService(IConfigRepository repository, IServerTagService tags,
    ITableRendererService tables, IMenuFactory menus, IUiChromeService chrome,
    IHostSelectorService hostSelector, IServerMenuService servers, IGroupSessionService sessions,
    FileTransferUi transfers, IMacroMenuService macros, IContainerMenuService containers,
    IContainerGroupService targetCommands, ILocalCredentialService localCredentials) : ITagMenuService
{
    public async Task Show()
    {
        while (true)
        {
            var config = repository.Load();
            var available = tags.GetAllTargets(config.Hosts);
            var palette = UiThemePalette.For(config.UI.Theme);
            void RenderView()
            {
                AnsiConsole.Clear();
                chrome.RenderBreadcrumb("Main", "Servers", "Tags");
                AnsiConsole.MarkupLine($"[{palette.Muted}]Tags select servers and saved containers across static groups. Each target uses its own tags.[/]");
                if (available.Count == 0) AnsiConsole.MarkupLine($"[{palette.Muted}]No target tags assigned yet.[/]");
            }
            var entries = available.Select(tag => new MenuEntry($"{Markup.Escape(tag)} [grey]({tags.FilterTargets(config.Hosts, [tag]).Count} targets)[/]",
                executeAsync: () => ShowTargets([tag]))).ToList();
            if (available.Count > 1)
                entries.Add(new("Filter by multiple tags", 'T', executeAsync: () => SelectTags(available, config)));
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menus.RunMenu("[bold yellow]Filter Targets by Tag[/]", entries, RenderView)) return;
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

    public Task ShowTag(string tag) => ShowTargets([tag]);

    private async Task ShowTargets(List<string> selectedTags)
    {
        var label = string.Join(" + ", selectedTags);
        while (true)
        {
            var config = repository.Load();
            var hosts = tags.Filter(config.Hosts, selectedTags);
            var targets = tags.FilterTargets(config.Hosts, selectedTags);
            var matchingContainers = targets.Where(target => target.Container is not null).Select(target => target.Container!).ToList();
            void RenderView()
            {
                AnsiConsole.Clear();
                chrome.RenderBreadcrumb("Main", "Servers", "Tags", label);
                tables.RenderGroup("Tags: " + label, hosts);
                if (matchingContainers.Count > 0) containers.RenderGroup("Tags: " + label, matchingContainers, config);
                AnsiConsole.MarkupLine($"[{UiThemePalette.For(config.UI.Theme).Muted}]{targets.Count} targets match all selected tags. Membership refreshes after each action.[/]");
            }
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
            if (matchingContainers.Count > 0)
            {
                entries.Add(new("Select container...", 'T', executeAsync: () => containers.SelectFromGroup(config, matchingContainers)));
                entries.Add(new("Exec matching containers", 'E', executeAsync: () => sessions.ConnectContainers(config, matchingContainers)));
                entries.Add(new("Operate matching containers", 'O', executeAsync: () => containers.ShowBatchActions(targets.Where(target => target.Container is not null).ToList())));
            }
            if (targets.Count > 0) entries.Add(new("Run command on all matching targets", 'X', executeAsync: () => RunTargets(targets)));
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menus.RunMenu("[bold yellow]Tag Actions[/]", entries, RenderView)) return;
        }
    }

    private async Task RunTargets(List<ConnectionTarget> targets)
    {
        var command = AnsiConsole.Prompt(new TextPrompt<string>("Command for all matching servers and containers ([grey]empty cancels[/]):").AllowEmpty());
        if (string.IsNullOrWhiteSpace(command)) return;
        foreach (var host in targets.Select(target => target.Host).DistinctBy(host => host.Id))
            if (!DockerCredentialPrompt.Prepare(host, localCredentials)) return;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            foreach (var result in await targetCommands.RunOnTargets(targets, command, ct: cancellation.Token))
            {
                AnsiConsole.WriteLine($"-- {result.Target.Label} ({result.State})");
                AnsiConsole.WriteLine(result.Error ?? result.Output);
            }
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private Task SelectServer(List<ServerHost> hosts)
    {
        var host = hostSelector.SelectHost(hosts, "Select a [green]server[/]");
        return host is null ? Task.CompletedTask : servers.ShowServerActions(host);
    }
}
