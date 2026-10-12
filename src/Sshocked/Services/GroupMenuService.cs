using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class GroupMenuService(IConfigRepository configRepository, IGroupManagementService groupManagement, IServerMenuService serverMenu, ITableRendererService tableRenderer, IHostSelectorService hostSelector, IConsoleHelperService consoleHelper, IMenuFactory menuFactory, INavigationService nav, IGroupConnectionService groupConnection, IContainerService containerService, IContainerGroupService containerGroupService, IProcessService processService, IRecentConnectionService recents, FileTransferUi transfers, IUiChromeService chrome) : IGroupMenuService
{
    public async Task Browse()
    {
        var groups = groupManagement.GetAll();
        if (groups.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No groups configured. Create one first.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        nav.Push(ViewType.GroupDetail);

        while (nav.Current == ViewType.GroupDetail)
        {
            var config = configRepository.Load();
            groups = groupManagement.GetAll();
            AnsiConsole.Clear();

            chrome.RenderBreadcrumb("Main", "Groups");
            var backLabel = MenuLabels.BackToMainMenu;
            var groupNames = groups.Select(g => g.Name).ToList();
            groupNames.Add(backLabel);

            var selectedName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold yellow]Select a Group[/] ([grey]clear search for Back[/]):")
                    .PageSize(10)
                    .EnableSearch()
                    .UseConverter(Markup.Escape).AddChoices(groupNames));

            if (selectedName == backLabel)
            {
                nav.Pop();
                return;
            }

            var group = groups.First(g => g.Name == selectedName);
            await ShowGroupDetail(config, group);
        }
    }

    private async Task ShowGroupDetail(AppConfig config, ServerGroup group)
    {
        nav.Push(ViewType.ServerDetail);

        while (nav.Current == ViewType.ServerDetail)
        {
            config = configRepository.Load();
            var groupHosts = config.Hosts.Where(h => h.GroupId == group.Id).ToList();
            var groupContainers = containerService.GetSavedByGroup(config, group.Id);

            AnsiConsole.Clear();
            chrome.RenderBreadcrumb("Main", "Groups", group.Name);
            AnsiConsole.MarkupLine($"[bold cyan]Group: {Markup.Escape(group.Name)}[/]");
            AnsiConsole.WriteLine();

            if (groupHosts.Count == 0 && groupContainers.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No servers or containers in this group.[/]");
            }
            else
            {
                if (groupHosts.Count > 0)
                {
                    tableRenderer.RenderGroup(group.Name + " (Servers)", groupHosts);
                }

                if (groupContainers.Count > 0)
                {
                    RenderContainerGroup(group.Name + " (Containers)", groupContainers, config);
                }
            }

            var menuEntries = new List<MenuEntry>();

            if (groupHosts.Count > 0)
            {
                menuEntries.Add(new MenuEntry("Select server...", 'S', executeAsync: () => { SelectServerFromGroup(config, group); return Task.CompletedTask; }));
                menuEntries.Add(new MenuEntry("Upload to all servers", 'U', executeAsync: () => transfers.UploadGroup(groupHosts)));
                menuEntries.Add(new MenuEntry("Download from all servers", 'O', executeAsync: () => transfers.DownloadGroup(groupHosts)));
                menuEntries.Add(new MenuEntry("Connect all servers", 'C', executeAsync: () => ConnectAllServers(groupHosts)));
                menuEntries.Add(new MenuEntry("Run command on servers", 'R', executeAsync: () => RunCommandOnServers(groupHosts)));
            }

            if (groupContainers.Count > 0)
            {
                menuEntries.Add(new MenuEntry("Select container...", 'T', executeAsync: () => { SelectContainerFromGroup(config, groupContainers); return Task.CompletedTask; }));
                menuEntries.Add(new MenuEntry("Exec all containers", 'E', executeAsync: () => ConnectAllContainers(config, groupContainers)));
                menuEntries.Add(new MenuEntry("Run command on containers", 'X', executeAsync: () => RunCommandOnContainers(config, groupContainers)));
            }

            menuEntries.Add(new MenuEntry("Back", 'B', actionType: MenuActionType.Back));

            var shouldContinue = await menuFactory.RunMenu("[bold yellow]Group Actions[/]", menuEntries);

            if (!shouldContinue)
            {
                nav.Pop();
                return;
            }
        }
    }

    private void RenderContainerGroup(string groupName, List<ContainerModel> containers, AppConfig config)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[cyan]{Markup.Escape(groupName)}[/]")
            .AddColumns([
                new TableColumn("Alias"),
                new TableColumn("Container"),
                new TableColumn("Server"),
                new TableColumn("Image")
            ]);

        foreach (var container in containers)
        {
            var server = containerGroupService.FindParentServer(config, container);
            table.AddRow(
                new Markup($"[bold]{Markup.Escape(container.Alias ?? container.Name)}[/]"),
                new Text(container.Name),
                new Text(server?.Alias ?? "?"),
                new Text(container.Image));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private void SelectContainerFromGroup(AppConfig config, List<ContainerModel> containers)
    {
        if (containers.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No containers available.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var names = containers.Select(c => c.Alias ?? c.Name).ToList();
        names.Add(MenuLabels.Back);

        var selectedName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [green]container[/] ([grey]clear search for Back[/]):")
                .PageSize(10)
                .EnableSearch()
                .UseConverter(Markup.Escape).AddChoices(names));

        if (selectedName == MenuLabels.Back)
        {
            return;
        }

        var container = containers.First(c => (c.Alias ?? c.Name) == selectedName);
        var server = containerGroupService.FindParentServer(config, container);

        if (server is not null)
        {
            ShowSavedContainerActionsFromGroup(server, container).Wait();
        }
    }

    private async Task ShowSavedContainerActionsFromGroup(ServerHost host, ContainerModel container)
    {
        var displayName = container.Alias ?? container.Name;

        var menuEntries = new List<MenuEntry>
        {
            new("Exec (sh)", 'E', executeAsync: () => consoleHelper.RunDockerExec(host, container)),
            new("Toggle favorite", 'F', executeAsync: () => { recents.ToggleFavorite(host, container); return Task.CompletedTask; }),
            new("Logs (-f)", 'L', executeAsync: () => consoleHelper.RunDockerLogs(host, container)),
            new("Back", 'B', actionType: MenuActionType.Back)
        };

        await menuFactory.RunMenu($"[bold yellow]{Markup.Escape(displayName)}[/]", menuEntries);
    }

    private async Task ConnectAllContainers(AppConfig config, List<ContainerModel> containers)
    {
        var strategy = config.DefaultConnectionStrategy;
        var strategyLabel = strategy == ConnectionStrategy.MultiTab ? "multi-tab" : "sequential";

        AnsiConsole.MarkupLine($"[green]Exec'ing {containers.Count} container(s) using {strategyLabel}...[/]");
        if (strategy == ConnectionStrategy.MultiTab)
        {
            await containerGroupService.ExecAllMultiTab(config, containers);
        }
        else
        {
            await containerGroupService.ExecAllSequential(config, containers);
        }
    }

    private async Task RunCommandOnContainers(AppConfig config, List<ContainerModel> containers)
    {
        AnsiConsole.MarkupLine("[grey]Interactive mode — type a command to run on all containers.[/]");
        AnsiConsole.MarkupLine("[grey]Leave empty or type [bold]exit[/] to quit.[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            Console.Write("$ ");
            var line = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            AnsiConsole.WriteLine();

            await containerGroupService.RunCommandOnAll(config, containers, trimmed);
        }
    }

    private void SelectServerFromGroup(AppConfig config, ServerGroup? groupFilter)
    {
        var hosts = groupFilter is not null
            ? config.Hosts.Where(h => h.GroupId == groupFilter.Id).ToList()
            : config.Hosts;

        if (hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No servers available.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var host = hostSelector.SelectHost(hosts, "Select a [green]server[/]");
        if (host is null)
        {
            return;
        }

        serverMenu.ShowServerActions(config, host);
    }

    private async Task RunCommandOnServers(List<ServerHost> hosts)
    {
        AnsiConsole.MarkupLine("[grey]Interactive mode — type a command to run on all servers.[/]");
        AnsiConsole.MarkupLine("[grey]Leave empty or type [bold]exit[/] to quit.[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            Console.Write("$ ");
            var line = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            AnsiConsole.WriteLine();

            foreach (var host in hosts)
            {
                var header = $"-- {host.Alias} ({host.User}@{host.HostName}) ";
                AnsiConsole.Markup($"[bold]{Markup.Escape(header)}[/]");
                Console.WriteLine(new string('-', Math.Max(1, 60 - header.Length)));

                try
                {
                    var output = await processService.RunSshCommand(host, trimmed);
                    Console.Write(output);

                    if (!output.EndsWith('\n'))
                    {
                        Console.WriteLine();
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                }

                AnsiConsole.WriteLine();
            }
        }
    }

    private async Task ConnectAllServers(List<ServerHost> hosts)
    {
        var config = configRepository.Load();
        var strategy = config.DefaultConnectionStrategy;
        var strategyLabel = strategy == ConnectionStrategy.MultiTab ? "multi-tab" : "sequential";

        AnsiConsole.MarkupLine($"[green]Connecting {hosts.Count} server(s) using {strategyLabel}...[/]");
        if (strategy == ConnectionStrategy.MultiTab)
        {
            await groupConnection.ConnectAllMultiTab(hosts);
        }
        else
        {
            await groupConnection.ConnectAllSequential(hosts);
        }
    }
}


