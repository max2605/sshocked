using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ContainerMenuService(IConfigRepository repository, IConsoleHelperService console,
    IContainerService containers, IContainerGroupService groups, IRecentConnectionService recents,
    IMenuFactory menu, IDockerService docker, ILocalCredentialService localCredentials) : IContainerMenuService
{
    public async Task Manage()
    {
        while (true)
        {
            var config = repository.Load();
            var targets = config.Hosts.SelectMany(host => host.SavedContainers.Concat(host.DockerInfo?.Containers ?? [])
                .DistinctBy(container => DockerCommandSyntax.Identifier(container)).Select(container => new ConnectionTarget(host, container))).ToList();
            var entries = new List<MenuEntry> { new("Scan selected servers", 'S', executeAsync: () => ScanHosts(config.Hosts)) };
            if (targets.Count > 0)
            {
                entries.Add(new("Select containers / operations", 'C', executeAsync: () => SelectTargets(targets)));
                entries.Add(new("Select container group", 'G', executeAsync: () => SelectGroup(config, targets)));
                entries.Add(new("Select container tags", 'T', executeAsync: () => SelectTags(targets)));
            }
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menu.RunMenu("[bold yellow]Docker Containers[/]", entries, () =>
            {
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine("[grey]Scan servers, then save containers to assign aliases, groups and tags. Ctrl+C cancels a running batch.[/]");
                RenderTargets(targets);
            })) return;
        }
    }

    public async Task ShowBatchActions(IReadOnlyList<ConnectionTarget> targets)
    {
        if (targets.Count == 0) return;
        var entries = Enum.GetValues<DockerOperation>().Select(operation => new MenuEntry(operation == DockerOperation.Remove ? "Remove from Docker..." : operation.ToString(),
            executeAsync: () => RunOperation(targets, operation))).ToList();
        entries.Add(new("Rescan selected servers", 'S', executeAsync: () => RunScan(targets.Select(target => target.Host).DistinctBy(host => host.Id).ToList())));
        entries.Add(new("Run command inside containers", 'R', executeAsync: () => RunCommand(targets)));
        if (targets.Count == 1)
        {
            var target = targets[0];
            entries.Add(new("Container details / save / edit", 'I', executeAsync: () => target.Container!.Id is null
                ? ShowScannedActions(target.Host, target.Container) : ShowSavedActions(target.Host, target.Container)));
        }
        entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
        await menu.RunMenu("[bold yellow]Selected Container Operations[/]", entries, () => RenderTargets(targets));
    }

    private async Task SelectTargets(List<ConnectionTarget> available)
    {
        var selected = AnsiConsole.Prompt(new MultiSelectionPrompt<ConnectionTarget>().Title("Select containers ([grey]Space selects; Enter applies; none cancels[/]):")
            .NotRequired().PageSize(12).UseConverter(target => Markup.Escape(target.Label)).AddChoices(available));
        if (selected.Count > 0) await ShowBatchActions(selected);
    }

    private async Task SelectGroup(AppConfig config, List<ConnectionTarget> targets)
    {
        var choices = config.Groups.Where(group => targets.Any(target => target.Container!.GroupId == group.Id)).ToList();
        if (choices.Count == 0) return;
        var name = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Select a container group:").UseConverter(Markup.Escape)
            .AddChoices(choices.Select(group => group.Name).Append(MenuLabels.Back)));
        if (name == MenuLabels.Back) return;
        await SelectTargets(targets.Where(target => target.Container!.GroupId == choices.First(group => group.Name == name).Id).ToList());
    }

    private async Task SelectTags(List<ConnectionTarget> targets)
    {
        var available = targets.SelectMany(target => target.Container!.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (available.Count == 0) return;
        var tags = AnsiConsole.Prompt(new MultiSelectionPrompt<string>().Title("Select container tags (matches every selected tag):")
            .NotRequired().UseConverter(Markup.Escape).AddChoices(available));
        if (tags.Count > 0) await SelectTargets(targets.Where(target => tags.All(tag => target.Container!.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))).ToList());
    }

    private async Task ScanHosts(List<ServerHost> hosts)
    {
        if (hosts.Count == 0) return;
        var selected = AnsiConsole.Prompt(new MultiSelectionPrompt<ServerHost>().Title("Select servers to scan:").NotRequired()
            .UseConverter(host => Markup.Escape(host.Alias)).AddChoices(hosts));
        if (selected.Count > 0) await RunScan(selected);
    }

    private async Task RunScan(List<ServerHost> hosts)
    {
        var prepared = hosts.Where(host => DockerCredentialPrompt.Prepare(host, localCredentials)).ToList();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var results = await docker.ScanMany(prepared, progress: new DockerConsoleProgress(), ct: cancellation.Token);
            var table = new Table().AddColumns("Server", "Result", "Details");
            foreach (var result in results) table.AddRow(new Text(result.Host.Alias), new Text(result.State.ToString()),
                new Text(result.Error ?? result.Info?.ScanWarning ?? $"{result.Info?.Containers.Count ?? 0} containers"));
            AnsiConsole.Write(table);
        }
        finally { Console.CancelKeyPress -= cancel; }
        console.WaitForKey();
    }

    private async Task RunOperation(IReadOnlyList<ConnectionTarget> targets, DockerOperation operation)
    {
        var removal = operation == DockerOperation.Remove;
        if (removal)
        {
            RenderTargets(targets);
            if (!AnsiConsole.Confirm($"Remove these {targets.Count} container(s) from Docker? Running containers must be stopped first. Saved entries are retained.", false)) return;
        }
        foreach (var host in targets.Select(target => target.Host).DistinctBy(host => host.Id))
            if (!DockerCredentialPrompt.Prepare(host, localCredentials)) return;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try { RenderResults(await docker.OperateMany(targets, operation, removal, progress: new DockerConsoleProgress(), ct: cancellation.Token)); }
        finally { Console.CancelKeyPress -= cancel; }
        console.WaitForKey();
    }

    private async Task RunCommand(IReadOnlyList<ConnectionTarget> targets)
    {
        var command = AnsiConsole.Prompt(new TextPrompt<string>("Container command ([grey]empty cancels[/]):").AllowEmpty());
        if (string.IsNullOrWhiteSpace(command)) return;
        foreach (var host in targets.Select(target => target.Host).DistinctBy(host => host.Id))
            if (!DockerCredentialPrompt.Prepare(host, localCredentials)) return;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try { RenderResults(await groups.RunOnTargets(targets, command, ct: cancellation.Token)); }
        finally { Console.CancelKeyPress -= cancel; }
        console.WaitForKey();
    }

    private static void RenderTargets(IReadOnlyList<ConnectionTarget> targets)
    {
        var table = new Table().AddColumns("Target", "Docker ID", "Status", "Group", "Tags");
        foreach (var target in targets) table.AddRow(new Text(target.Label), new Text(target.Container!.ContainerId),
            new Text(target.Container.Status), new Text(target.Container.GroupId), new Text(string.Join(", ", target.Container.Tags)));
        AnsiConsole.Write(table);
    }

    private static void RenderResults(IReadOnlyList<DockerActionResult> results)
    {
        var table = new Table().AddColumns("Target", "Result", "Details");
        foreach (var result in results) table.AddRow(new Text(result.Target.Label), new Text(result.State.ToString()), new Text(result.Error ?? result.Output.Trim()));
        AnsiConsole.Write(table);
    }

    public async Task ShowScannedActions(ServerHost host, ContainerModel container)
    {
        var currentHost = repository.Load().Hosts.First(h => h.Id == host.Id);
        var isSaved = currentHost.SavedContainers.Any(saved => saved.ContainerId == container.ContainerId || saved.Name == container.Name);
        var entries = BuildSessionEntries(host, container);
        if (!isSaved)
        {
            entries.Add(new("Save...", 'V', executeAsync: () =>
            {
                containers.Save(host, container);
                return Task.CompletedTask;
            }));
        }
        entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
        await menu.RunMenu($"[bold yellow]{Markup.Escape(container.Name)}[/]", entries);
    }

    public async Task ShowSavedActions(ServerHost host, ContainerModel container, bool allowManagement = true)
    {
        var displayName = container.Alias ?? container.Name;
        var entries = BuildSessionEntries(host, container);
        entries.Insert(1, new("Toggle favorite", 'F', executeAsync: () =>
        {
            recents.ToggleFavorite(host, container);
            return Task.CompletedTask;
        }));
        if (allowManagement)
        {
            entries.Add(new("Edit", 'I', executeAsync: () =>
            {
                containers.Edit(host, container);
                return Task.CompletedTask;
            }));
            entries.Add(new("Delete", 'D', executeAsync: () =>
            {
                if (containers.Delete(host, container))
                    AnsiConsole.MarkupLine($"[green]✓[/] Container '[cyan]{Markup.Escape(displayName)}[/]' deleted.");
                else AnsiConsole.MarkupLine("[grey]Deletion cancelled.[/]");
                console.WaitForKey();
                return Task.CompletedTask;
            }));
        }
        entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
        await menu.RunMenu($"[bold yellow]{Markup.Escape(displayName)}[/]", entries);
    }

    public async Task SelectFromGroup(AppConfig config, List<ContainerModel> available)
    {
        if (available.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No containers available.[/]");
            console.WaitForKey();
            return;
        }
        var names = available.Select(container => container.Alias ?? container.Name).Append(MenuLabels.Back);
        var selectedName = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Select a [green]container[/] ([grey]clear search for Back[/]):")
            .PageSize(10).EnableSearch().UseConverter(Markup.Escape).AddChoices(names));
        if (selectedName == MenuLabels.Back) return;
        var container = available.First(container => (container.Alias ?? container.Name) == selectedName);
        var host = groups.FindParentServer(config, container);
        if (host is not null) await ShowSavedActions(host, container, false);
    }

    public void RenderGroup(string groupName, List<ContainerModel> available, AppConfig config)
    {
        var table = new Table().Border(TableBorder.Rounded)
            .Title($"[cyan]{Markup.Escape(groupName)}[/]")
            .AddColumns(new TableColumn("Alias"), new TableColumn("Container"), new TableColumn("Server"), new TableColumn("Image"));
        foreach (var container in available)
        {
            var server = groups.FindParentServer(config, container);
            table.AddRow(new Markup($"[bold]{Markup.Escape(container.Alias ?? container.Name)}[/]"),
                new Text(container.Name), new Text(server?.Alias ?? "?"), new Text(container.Image));
        }
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private List<MenuEntry> BuildSessionEntries(ServerHost host, ContainerModel container) =>
    [
        new("Exec (sh)", 'E', executeAsync: () => console.RunDockerExec(host, container)),
        new("Logs (-f)", 'L', executeAsync: () => console.RunDockerLogs(host, container)),
        new("Docker operations...", 'O', executeAsync: () => ShowBatchActions([new(host, container)]))
    ];
}
