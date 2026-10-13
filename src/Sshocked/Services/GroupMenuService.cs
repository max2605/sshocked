using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class GroupMenuService(IConfigRepository configRepository, IGroupManagementService groupManagement,
    IServerMenuService serverMenu, ITableRendererService tableRenderer, IHostSelectorService hostSelector,
    IConsoleHelperService consoleHelper, IMenuFactory menuFactory, INavigationService nav,
    IContainerService containerService, IGroupSessionService sessions, IContainerMenuService containerMenu,
    FileTransferUi transfers, IUiChromeService chrome, IMacroMenuService macros) : IGroupMenuService
{
    public async Task Browse()
    {
        nav.Push(ViewType.GroupDetail);
        try
        {
            while (true)
            {
                var config = configRepository.Load();
                var palette = UiThemePalette.For(config.UI.Theme);
                AnsiConsole.Clear();
                chrome.RenderBreadcrumb("Main", "Groups");
                tableRenderer.RenderServerTable(config);
                if (config.Groups.Count == 0)
                    AnsiConsole.MarkupLine($"[{palette.Muted}]No groups configured. Use Create Group to add one.[/]");

                var entries = config.Groups.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new MenuEntry(Markup.Escape(group.Name), executeAsync: () => ShowGroupDetail(group.Id))).ToList();
                entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
                if (!await menuFactory.RunMenu("[bold yellow]Browse / Manage Groups[/]", entries)) return;
            }
        }
        finally { nav.Pop(); }
    }

    private async Task ShowGroupDetail(string groupId)
    {
        nav.Push(ViewType.ServerDetail);
        try
        {
            while (true)
            {
                var config = configRepository.Load();
                var group = config.Groups.FirstOrDefault(item => item.Id == groupId);
                if (group is null) return;
                var groupHosts = config.Hosts.Where(host => host.GroupId == group.Id).ToList();
                var groupContainers = containerService.GetSavedByGroup(config, group.Id);
                var palette = UiThemePalette.For(config.UI.Theme);
                AnsiConsole.Clear();
                chrome.RenderBreadcrumb("Main", "Groups", group.Name);
                AnsiConsole.MarkupLine($"[bold {palette.Accent}]Group: {Markup.Escape(group.Name)}[/]");
                AnsiConsole.WriteLine();
                if (groupHosts.Count == 0 && groupContainers.Count == 0)
                    AnsiConsole.MarkupLine($"[{palette.Muted}]No servers or containers in this group.[/]");
                if (groupHosts.Count > 0) tableRenderer.RenderGroup(group.Name + " (Servers)", groupHosts);
                if (groupContainers.Count > 0) containerMenu.RenderGroup(group.Name + " (Containers)", groupContainers, config);

                var entries = new List<MenuEntry>();
                if (groupHosts.Count > 0)
                {
                    entries.Add(new("Select server...", 'S', executeAsync: () => SelectServer(groupHosts)));
                    entries.Add(new("Connect all servers", 'C', executeAsync: () => sessions.ConnectServers(config, groupHosts)));
                    entries.Add(new("Run command on servers", 'R', executeAsync: () => sessions.RunServerCommands(groupHosts)));
                    entries.Add(new("Run macro on servers", 'M', executeAsync: () => macros.Run(groupHosts)));
                    entries.Add(new("Upload to all servers", 'U', executeAsync: () => transfers.UploadGroup(groupHosts)));
                    entries.Add(new("Download from all servers", 'O', executeAsync: () => transfers.DownloadGroup(groupHosts)));
                }
                if (groupContainers.Count > 0)
                {
                    entries.Add(new("Select container...", 'T', executeAsync: () => containerMenu.SelectFromGroup(config, groupContainers)));
                    entries.Add(new("Exec all containers", 'E', executeAsync: () => sessions.ConnectContainers(config, groupContainers)));
                    entries.Add(new("Run command on containers", 'X', executeAsync: () => sessions.RunContainerCommands(config, groupContainers)));
                }
                entries.Add(new("Rename group", 'N', executeAsync: () => { RenameGroup(group); return Task.CompletedTask; }));
                entries.Add(new("Delete group", 'D', executeAsync: () => { DeleteGroup(group); return Task.CompletedTask; }));
                entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
                if (!await menuFactory.RunMenu("[bold yellow]Group Actions[/]", entries)) return;
            }
        }
        finally { nav.Pop(); }
    }

    private Task SelectServer(List<ServerHost> hosts)
    {
        var host = hostSelector.SelectHost(hosts, "Select a [green]server[/]");
        return host is null ? Task.CompletedTask : serverMenu.ShowServerActions(host);
    }

    private void RenameGroup(ServerGroup group)
    {
        var name = AnsiConsole.Prompt(new TextPrompt<string>("New [green]group name[/] (empty cancels):").AllowEmpty());
        if (string.IsNullOrWhiteSpace(name)) return;
        groupManagement.Rename(group.Id, name.Trim());
    }

    private void DeleteGroup(ServerGroup group)
    {
        if (!AnsiConsole.Confirm($"Delete group '[yellow]{Markup.Escape(group.Name)}[/]'? Servers and containers will be kept without a group.", false)) return;
        groupManagement.Delete(group.Id);
        AnsiConsole.MarkupLine($"[green]✓[/] Group '[cyan]{Markup.Escape(group.Name)}[/]' deleted; servers and containers retained.");
        consoleHelper.WaitForKey();
    }
}
