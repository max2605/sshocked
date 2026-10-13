using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class QuickConnectionMenuService(IGroupManagementService groups, IContainerService containers,
    IHostSelectorService hostSelector, IConsoleHelperService console, IMenuFactory menu,
    IGroupSessionService sessions, IContainerMenuService containerMenu,
    IManagedCommandSessionService commandSessions) : IQuickConnectionMenuService
{
    public async Task ConnectServer(AppConfig config)
    {
        if (config.Hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No hosts to connect to.[/]");
            console.WaitForKey();
            return;
        }
        var host = hostSelector.SelectHost(config.Hosts, "Select a [green]server[/] to connect to");
        if (host is null) return;
        var scanned = host.DockerInfo?.Containers ?? [];
        var saved = host.SavedContainers;
        if (scanned.Count == 0 && saved.Count == 0)
        {
            await console.RunSsh(host);
            return;
        }
        var entries = new List<MenuEntry>
        {
            new("Connect (SSH)", 'S', executeAsync: () => console.RunSsh(host))
        };
        foreach (var container in scanned)
        {
            var isSaved = saved.Any(item => item.ContainerId == container.ContainerId || item.Name == container.Name);
            var marker = isSaved ? "[green]✓[/] " : "";
            entries.Add(new($"{marker}{Markup.Escape(container.Name)}  [grey]({Markup.Escape(container.Image)})[/]",
                executeAsync: () => containerMenu.ShowScannedActions(host, container)));
        }
        foreach (var container in saved)
        {
            var name = container.Alias ?? container.Name;
            entries.Add(new($"[bold][cyan]*[/] {Markup.Escape(name)}[/]  [grey]({Markup.Escape(container.Image)})[/]",
                executeAsync: () => containerMenu.ShowSavedActions(host, container)));
        }
        entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
        await menu.RunMenu($"[bold yellow]Connect to {Markup.Escape(host.Alias)}[/]", entries);
    }

    public async Task ConnectGroup(AppConfig config)
    {
        var group = await SelectGroup("[bold yellow]Connect Group[/]");
        if (group is null) return;
        var hosts = config.Hosts.Where(host => host.GroupId == group.Id).ToList();
        var savedContainers = containers.GetSavedByGroup(config, group.Id);
        if (!HasTargets(group, hosts, savedContainers)) return;
        if (hosts.Count > 0) await sessions.ConnectServers(config, hosts);
        if (savedContainers.Count > 0)
        {
            if (hosts.Count > 0) console.WaitForKey();
            await sessions.ConnectContainers(config, savedContainers);
        }
    }

    public async Task RunGroupCommand(AppConfig config)
    {
        var entries = new List<MenuEntry>
        {
            new("Single server / record macro", 'S', executeAsync: () => RunServerCommand(config)),
            new("Server / container group", 'G', executeAsync: () => RunForGroup(config)),
            new("Back", 'B', actionType: MenuActionType.Back)
        };
        await menu.RunMenu("[bold yellow]Run command on...[/]", entries);
    }

    private Task RunServerCommand(AppConfig config)
    {
        var host = hostSelector.SelectHost(config.Hosts, "Select a [green]server[/] for commands");
        return host is null ? Task.CompletedTask : commandSessions.Show(host);
    }

    private async Task RunForGroup(AppConfig config)
    {
        var group = await SelectGroup("[bold yellow]Select Group[/]");
        if (group is null) return;
        var hosts = config.Hosts.Where(host => host.GroupId == group.Id).ToList();
        var savedContainers = containers.GetSavedByGroup(config, group.Id);
        if (!HasTargets(group, hosts, savedContainers)) return;

        var runOnServers = false;
        var entries = new List<MenuEntry>();
        if (hosts.Count > 0)
        {
            entries.Add(new("Run on servers", 'S', executeAsync: () =>
            {
                runOnServers = true;
                return Task.CompletedTask;
            }));
        }
        if (savedContainers.Count > 0)
        {
            entries.Add(new("Run on containers", 'T', executeAsync: () =>
            {
                runOnServers = false;
                return Task.CompletedTask;
            }));
        }
        entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
        if (!await menu.RunMenu("[bold yellow]Run command on...[/]", entries)) return;
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold cyan]Group: {Markup.Escape(group.Name)}[/]");
        AnsiConsole.WriteLine();
        if (runOnServers) await sessions.RunServerCommands(hosts);
        else await sessions.RunContainerCommands(config, savedContainers);
    }

    private async Task<ServerGroup?> SelectGroup(string title)
    {
        var available = groups.GetAll();
        if (available.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No groups configured. Create one first.[/]");
            console.WaitForKey();
            return null;
        }
        ServerGroup? selected = null;
        var entries = available.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MenuEntry(Markup.Escape(group.Name), executeAsync: () =>
            {
                selected = group;
                return Task.CompletedTask;
            })).ToList();
        entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
        return await menu.RunMenu(title, entries) ? selected : null;
    }

    private bool HasTargets(ServerGroup group, List<ServerHost> hosts, List<ContainerModel> savedContainers)
    {
        if (hosts.Count > 0 || savedContainers.Count > 0) return true;
        AnsiConsole.MarkupLine($"[red]No servers or containers in group '[cyan]{Markup.Escape(group.Name)}[/]'.[/]");
        console.WaitForKey();
        return false;
    }
}
