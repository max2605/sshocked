using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MainMenuService(IConfigRepository configRepository, IServerCrudService serverCrud, IGroupManagementService groupManagement, IGroupMenuService groupMenu, IServerMenuService serverMenu, ITableRendererService tableRenderer, IHostSelectorService hostSelector, IConsoleHelperService consoleHelper, IGroupConnectionService groupConnection, IContainerService containerService, IContainerGroupService containerGroupService, IProcessService processService, IMenuFactory menuFactory, IQuickAccessMenuService quickAccess, IRecentConnectionService recents, ISshConfigSyncMenuService sshConfigSyncMenu, ISecretConfigurationUi secretUi, SessionRecordingConfigurationUi recordingUi, ILogger<MainMenuService> logger) : IMainMenuService
{

    public async Task Show()
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            logger.LogWarning("Terminal is not interactive -- skipping main menu");
            return;
        }

        var running = true;

        while (running)
        {
            var config = configRepository.Load();
            AnsiConsole.Clear();
            tableRenderer.RenderServerTable(config);

            var menuEntries = BuildMainMenuEntries(config);
            running = await menuFactory.RunMenu("[bold yellow]Main Menu[/]", menuEntries);
        }
    }

    private List<MenuEntry> BuildMainMenuEntries(AppConfig config)
    {
        return new List<MenuEntry>
        {
            new("Favorites", 'F', executeAsync: quickAccess.ShowFavorites),
            new("Recent connections", 'H', executeAsync: quickAccess.ShowRecent),
            new("Quick Connect", 'C', executeAsync: () => ConnectToServer(config)),
            new("Connect Group", 'G', executeAsync: () => ConnectToGroup(config)),
            new("Run command", 'X', executeAsync: () => RunCommandOnGroup(config)),
            new("SSH Config Sync", 'Y', executeAsync: sshConfigSyncMenu.Show),
            new("Manage Servers", 'S', executeAsync: () => ShowServersMenu(config)),
            new("Manage Groups", 'R', executeAsync: () => ShowGroupsMenu(config)),
            new("Settings", 'T', executeAsync: () => ShowSettingsMenu(config)),
            new("Exit", 'E', actionType: MenuActionType.Exit)
        };
    }

    private async Task ShowSettingsMenu(AppConfig config)
    {
        var running = true;

        while (running)
        {
            config = configRepository.Load();
            AnsiConsole.Clear();

            var currentStrategy = config.DefaultConnectionStrategy == ConnectionStrategy.MultiTab
                ? "Multi-Tab"
                : "Sequential";

            AnsiConsole.MarkupLine("[bold yellow]Settings[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  Default connection strategy: [green]{currentStrategy}[/]");
            AnsiConsole.WriteLine();

            var menuEntries = new List<MenuEntry>
            {
                new("Manage secret providers", 'P', executeAsync: secretUi.Show),
                new("Session recording", 'O', executeAsync: recordingUi.Show),
                new("Global connection hooks", 'K', executeAsync: () =>
                {
                    config.GlobalHooks = HookConfigurationUi.ConfigureGlobal(config.GlobalHooks);
                    configRepository.Save(config);
                    return Task.CompletedTask;
                }),
                new("Toggle recent connection tracking", 'H', executeAsync: () =>
                {
                    config.TrackRecentConnections = !config.TrackRecentConnections;
                    configRepository.Save(config);
                    return Task.CompletedTask;
                }),
                new("Default to Multi-Tab (parallel)", 'M', executeAsync: () =>
                {
                    config.DefaultConnectionStrategy = ConnectionStrategy.MultiTab;
                    configRepository.Save(config);
                    AnsiConsole.MarkupLine("[green]✓ Default strategy set to Multi-Tab[/]");
                    consoleHelper.WaitForKey();
                    return Task.CompletedTask;
                }),
                new("Default to Sequential (one after another)", 'Q', executeAsync: () =>
                {
                    config.DefaultConnectionStrategy = ConnectionStrategy.Sequential;
                    configRepository.Save(config);
                    AnsiConsole.MarkupLine("[green]✓ Default strategy set to Sequential[/]");
                    consoleHelper.WaitForKey();
                    return Task.CompletedTask;
                }),
                new("Back", 'B', actionType: MenuActionType.Back)
            };

            running = await menuFactory.RunMenu("[bold yellow]Settings[/]", menuEntries);
        }
    }

    private async Task ShowServersMenu(AppConfig config)
    {
        var running = true;

        while (running)
        {
            config = configRepository.Load();
            AnsiConsole.Clear();
            tableRenderer.RenderServerTable(config);

            var menuEntries = new List<MenuEntry>
            {
                new("Browse / Manage", 'S', executeAsync: () => serverMenu.SelectServer()),
                new("Add Server", 'A', executeAsync: () => { AddServer(); return Task.CompletedTask; }),
                new("Back", 'B', actionType: MenuActionType.Back)
            };

            running = await menuFactory.RunMenu("[bold yellow]Manage Servers[/]", menuEntries);
        }
    }

    private async Task ShowGroupsMenu(AppConfig config)
    {
        var running = true;

        while (running)
        {
            config = configRepository.Load();
            AnsiConsole.Clear();
            tableRenderer.RenderServerTable(config);

            var menuEntries = new List<MenuEntry>
            {
                new("Browse / Manage", 'S', executeAsync: () => groupMenu.Browse()),
                new("Create Group", 'A', executeAsync: () => { CreateNewGroup(config); return Task.CompletedTask; }),
                new("Back", 'B', actionType: MenuActionType.Back)
            };

            running = await menuFactory.RunMenu("[bold yellow]Manage Groups[/]", menuEntries);
        }
    }

    private async Task ConnectToGroup(AppConfig config)
    {
        var groups = groupManagement.GetAll();
        if (groups.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No groups configured. Create one first.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var groupNames = groups.Select(g => g.Name).ToList();
        groupNames.Add(MenuLabels.Back);

        var selectedName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [green]group[/] to connect ([grey]clear search for Back[/]):")
                .PageSize(10)
                .EnableSearch()
                .AddChoices(groupNames));

        if (selectedName == MenuLabels.Back)
        {
            return;
        }

        var group = groups.First(g => g.Name == selectedName);
        var hosts = config.Hosts.Where(h => h.GroupId == group.Id).ToList();
        var containers = containerService.GetSavedByGroup(config, group.Id);

        if (hosts.Count == 0 && containers.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]No servers or containers in group '[cyan]{Markup.Escape(group.Name)}[/]'.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var strategy = config.DefaultConnectionStrategy;
        var strategyLabel = strategy == ConnectionStrategy.MultiTab ? "multi-tab" : "sequential";

        if (hosts.Count > 0)
        {
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

        if (containers.Count > 0)
        {
            if (hosts.Count > 0)
            {
                consoleHelper.WaitForKey();
            }
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
    }

    private async Task RunCommandOnGroup(AppConfig config)
    {
        var groups = groupManagement.GetAll();
        if (groups.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No groups configured. Create one first.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var groupNames = groups.Select(g => g.Name).ToList();
        groupNames.Add(MenuLabels.Back);

        var selectedName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [green]group[/] ([grey]clear search for Back[/]):")
                .PageSize(10)
                .EnableSearch()
                .AddChoices(groupNames));

        if (selectedName == MenuLabels.Back)
        {
            return;
        }

        var group = groups.First(g => g.Name == selectedName);
        var hosts = config.Hosts.Where(h => h.GroupId == group.Id).ToList();
        var containers = containerService.GetSavedByGroup(config, group.Id);

        if (hosts.Count == 0 && containers.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]No servers or containers in group '[cyan]{Markup.Escape(group.Name)}[/]'.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var targetMenu = new List<MenuEntry>();
        if (hosts.Count > 0)
        {
            targetMenu.Add(new MenuEntry("Run on servers", 'S'));
        }
        if (containers.Count > 0)
        {
            targetMenu.Add(new MenuEntry("Run on containers", 'T'));
        }
        targetMenu.Add(new MenuEntry("Back", 'B', actionType: MenuActionType.Back));

        var targetChoice = await menuFactory.RunMenu("[bold yellow]Run command on...[/]", targetMenu);
        if (!targetChoice)
        {
            return;
        }

        // Note: This is simplified - in a real scenario we'd track which option was selected
        // For now, we check if we have both and ask, otherwise use what's available
        bool runOnServers;
        if (hosts.Count > 0 && containers.Count == 0)
        {
            runOnServers = true;
        }
        else if (containers.Count > 0 && hosts.Count == 0)
        {
            runOnServers = false;
        }
        else
        {
            // Re-prompt with selection
            var target = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Run on [green]servers[/] or [green]containers[/]?")
                    .AddChoices(["Servers", "Containers"]));
            runOnServers = target == "Servers";
        }

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold cyan]Group: {Markup.Escape(group.Name)}[/]");
        AnsiConsole.WriteLine();
        if (runOnServers)
        {
            AnsiConsole.MarkupLine("[grey]Interactive mode — type a command to run on all servers.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Interactive mode — type a command to run on all containers.[/]");
        }
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

            if (runOnServers)
            {
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
            else
            {
                await containerGroupService.RunCommandOnAll(config, containers, trimmed);
            }
        }
    }

    private async Task ConnectToServer(AppConfig config)
    {
        if (config.Hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No hosts to connect to.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var host = hostSelector.SelectHost(config.Hosts, "Select a [green]server[/] to connect to");
        if (host is null)
        {
            return;
        }

        var scannedContainers = host.DockerInfo?.Containers ?? [];
        var savedContainers = host.SavedContainers;

        if (scannedContainers.Count > 0 || savedContainers.Count > 0)
        {
            var menuEntries = new List<MenuEntry>
            {
                new("Connect (SSH)", 'S', executeAsync: () => consoleHelper.RunSsh(host))
            };

            foreach (var container in scannedContainers)
            {
                var isSaved = savedContainers.Any(sc => sc.ContainerId == container.ContainerId || sc.Name == container.Name);
                var savedMarker = isSaved ? "[green]\u2713[/] " : "";
                var capturedContainer = container;
                menuEntries.Add(new MenuEntry(
                    $"{savedMarker}{Markup.Escape(container.Name)}  [grey]({container.Image})[/]",
                    executeAsync: () => ShowScannedContainerActions(host, capturedContainer)));
            }

            foreach (var container in savedContainers)
            {
                var capturedContainer = container;
                var displayName = container.Alias ?? container.Name;
                menuEntries.Add(new MenuEntry(
                    $"[bold][cyan]*[/] {Markup.Escape(displayName)}[/]  [grey]({container.Image})[/]",
                    executeAsync: () => ShowSavedContainerActions(host, capturedContainer)));
            }

            menuEntries.Add(new MenuEntry("Back", 'B', actionType: MenuActionType.Back));

            await menuFactory.RunMenu($"[bold yellow]Connect to {Markup.Escape(host.Alias)}[/]", menuEntries);
            return;
        }

        await consoleHelper.RunSsh(host);
    }

    private async Task ShowScannedContainerActions(ServerHost host, ContainerModel container)
    {
        var config = configRepository.Load();
        var currentHost = config.Hosts.First(h => h.Id == host.Id);
        var isSaved = currentHost.SavedContainers.Any(
            sc => sc.ContainerId == container.ContainerId || sc.Name == container.Name);

        var menuEntries = new List<MenuEntry>
        {
            new("Exec (sh)", 'E', executeAsync: () => consoleHelper.RunDockerExec(host, container)),
            new("Logs (-f)", 'L', executeAsync: () => consoleHelper.RunDockerLogs(host, container))
        };

        if (!isSaved)
        {
            menuEntries.Add(new MenuEntry("Save...", 'V', executeAsync: () =>
            {
                containerService.Save(host, container);
                return Task.CompletedTask;
            }));
        }

        menuEntries.Add(new MenuEntry("Back", 'B', actionType: MenuActionType.Back));

        await menuFactory.RunMenu($"[bold yellow]{Markup.Escape(container.Name)}[/]", menuEntries);
    }

    private async Task ShowSavedContainerActions(ServerHost host, ContainerModel container)
    {
        var displayName = container.Alias ?? container.Name;

        var menuEntries = new List<MenuEntry>
        {
            new("Exec (sh)", 'E', executeAsync: () => consoleHelper.RunDockerExec(host, container)),
            new("Toggle favorite", 'F', executeAsync: () => { recents.ToggleFavorite(host, container); return Task.CompletedTask; }),
            new("Logs (-f)", 'L', executeAsync: () => consoleHelper.RunDockerLogs(host, container)),
            new("Edit", 'I', executeAsync: () =>
            {
                containerService.Edit(host, container);
                return Task.CompletedTask;
            }),
            new("Delete", 'D', executeAsync: () =>
            {
                if (containerService.Delete(host, container))
                {
                    AnsiConsole.MarkupLine($"[green]\u2713[/] Container '[cyan]{Markup.Escape(displayName)}[/]' deleted.");
                    consoleHelper.WaitForKey();
                }
                else
                {
                    AnsiConsole.MarkupLine("[grey]Deletion cancelled.[/]");
                    consoleHelper.WaitForKey();
                }
                return Task.CompletedTask;
            }),
            new("Back", 'B', actionType: MenuActionType.Back)
        };

        await menuFactory.RunMenu($"[bold yellow]{Markup.Escape(displayName)}[/]", menuEntries);
    }

    private void EditServer(AppConfig config)
    {
        if (config.Hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No hosts to edit.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var host = hostSelector.SelectHost(config.Hosts, "Select a [green]server[/] to edit");
        if (host is null)
        {
            return;
        }

        serverCrud.Edit(host);
        AnsiConsole.MarkupLine($"[green]\u2713[/] Server '[cyan]{Markup.Escape(host.Alias)}[/]' updated.");
        consoleHelper.WaitForKey();
    }

    private void DeleteServer(AppConfig config)
    {
        if (config.Hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No hosts to delete.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var host = hostSelector.SelectHost(config.Hosts, "Select a [green]server[/] to delete");
        if (host is null)
        {
            return;
        }

        if (serverCrud.Delete(host))
        {
            AnsiConsole.MarkupLine($"[green]\u2713[/] Server '[cyan]{Markup.Escape(host.Alias)}[/]' deleted.");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Deletion cancelled.[/]");
        }
        consoleHelper.WaitForKey();
    }

    private void AddServer()
    {
        var host = serverCrud.Add();
        if (host is not null)
        {
            AnsiConsole.MarkupLine($"[green]\u2713[/] Server '[cyan]{Markup.Escape(host.Alias)}[/]' added.");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Add server cancelled.[/]");
        }
        consoleHelper.WaitForKey();
    }

    private void CreateNewGroup(AppConfig config)
    {
        var name = AnsiConsole.Ask<string>("Enter [green]group name[/] (or leave empty to cancel):");

        if (string.IsNullOrWhiteSpace(name))
        {
            AnsiConsole.MarkupLine("[grey]Create group cancelled.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        groupManagement.Create(name.Trim());
        AnsiConsole.MarkupLine($"[green]\u2713[/] Group '[cyan]{name.Trim()}[/]' created.");
        consoleHelper.WaitForKey();
    }

    private void RenameGroup(AppConfig config)
    {
        var groups = groupManagement.GetAll();
        if (groups.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No groups to rename.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var groupNames = groups.Select(g => g.Name).ToList();
        var selectedName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [green]group[/] to rename:")
                .PageSize(10)
                .AddChoices(groupNames));

        var group = groups.First(g => g.Name == selectedName);
        var newName = AnsiConsole.Ask<string>($"Enter new name for [cyan]{Markup.Escape(group.Name)}[/] (or leave empty to cancel):");

        if (string.IsNullOrWhiteSpace(newName))
        {
            AnsiConsole.MarkupLine("[grey]Rename cancelled.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        groupManagement.Rename(group.Id, newName.Trim());
        AnsiConsole.MarkupLine($"[green]\u2713[/] Group renamed to '[cyan]{Markup.Escape(newName.Trim())}[/]'.");
        consoleHelper.WaitForKey();
    }

    private void DeleteGroup(AppConfig config)
    {
        var groups = groupManagement.GetAll();
        if (groups.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No groups to delete.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        var groupNames = groups.Select(g => g.Name).ToList();
        var selectedName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [green]group[/] to delete:")
                .PageSize(10)
                .AddChoices(groupNames));

        var group = groups.First(g => g.Name == selectedName);

        if (AnsiConsole.Confirm($"Delete group '[cyan]{Markup.Escape(group.Name)}[/]'? Hosts in this group will become uncategorized."))
        {
            groupManagement.Delete(group.Id);
            AnsiConsole.MarkupLine($"[green]\u2713[/] Group '[cyan]{Markup.Escape(group.Name)}[/]' deleted.");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Deletion cancelled.[/]");
        }
        consoleHelper.WaitForKey();
    }
}


