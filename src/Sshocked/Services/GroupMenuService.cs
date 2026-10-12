using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class GroupMenuService(IConfigRepository configRepository, IGroupManagementService groupManagement, IServerMenuService serverMenu, ITableRendererService tableRenderer, IHostSelectorService hostSelector, IConsoleHelperService consoleHelper, IMenuFactory menuFactory, INavigationService nav, IContainerService containerService, IGroupSessionService sessions, IContainerMenuService containerMenu, FileTransferUi transfers, IUiChromeService chrome) : IGroupMenuService
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
                    containerMenu.RenderGroup(group.Name + " (Containers)", groupContainers, config);
                }
            }

            var menuEntries = new List<MenuEntry>();

            if (groupHosts.Count > 0)
            {
                menuEntries.Add(new MenuEntry("Select server...", 'S', executeAsync: () => SelectServerFromGroup(config, group)));
                menuEntries.Add(new MenuEntry("Upload to all servers", 'U', executeAsync: () => transfers.UploadGroup(groupHosts)));
                menuEntries.Add(new MenuEntry("Download from all servers", 'O', executeAsync: () => transfers.DownloadGroup(groupHosts)));
                menuEntries.Add(new MenuEntry("Connect all servers", 'C', executeAsync: () => sessions.ConnectServers(configRepository.Load(), groupHosts)));
                menuEntries.Add(new MenuEntry("Run command on servers", 'R', executeAsync: () => sessions.RunServerCommands(groupHosts)));
            }

            if (groupContainers.Count > 0)
            {
                menuEntries.Add(new MenuEntry("Select container...", 'T', executeAsync: () => containerMenu.SelectFromGroup(config, groupContainers)));
                menuEntries.Add(new MenuEntry("Exec all containers", 'E', executeAsync: () => sessions.ConnectContainers(config, groupContainers)));
                menuEntries.Add(new MenuEntry("Run command on containers", 'X', executeAsync: () => sessions.RunContainerCommands(config, groupContainers)));
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

    private async Task SelectServerFromGroup(AppConfig config, ServerGroup groupFilter)
    {
        var hosts = config.Hosts.Where(host => host.GroupId == groupFilter.Id).ToList();

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

        await serverMenu.ShowServerActions(host);
    }

}
