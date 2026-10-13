using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MainMenuService(IConfigRepository repository, IServerCrudService servers,
    IGroupManagementService groups, IGroupMenuService groupMenu, IServerMenuService serverMenu,
    ITableRendererService tableRenderer, IConsoleHelperService console, IMenuFactory menu,
    IQuickAccessMenuService quickAccess, ISshConfigSyncMenuService sshConfigSyncMenu,
    IQuickConnectionMenuService connections, ISettingsMenuService settings,
    IUiChromeService chrome, ILogger<MainMenuService> logger, ITagMenuService tags, IMacroMenuService macros) : IMainMenuService
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
            var config = repository.Load();
            RenderMain(config);
            var entries = new List<MenuEntry>
            {
                new("Favorites", 'F', executeAsync: quickAccess.ShowFavorites),
                new("Recent connections", 'H', executeAsync: quickAccess.ShowRecent),
                new("Quick Connect", 'C', executeAsync: () => connections.ConnectServer(config)),
                new("Connect Group", 'G', executeAsync: () => connections.ConnectGroup(config)),
                new("Run command", 'X', executeAsync: () => connections.RunGroupCommand(config)),
                new("Macros", 'M', executeAsync: macros.Show),
                new("SSH Config Sync", 'Y', executeAsync: sshConfigSyncMenu.Show),
                new("Manage Servers", 'S', executeAsync: ShowServersMenu),
                new("Manage Groups", 'R', executeAsync: ShowGroupsMenu),
                new("Settings", 'T', executeAsync: settings.Show),
                new("Exit", 'E', actionType: MenuActionType.Exit)
            };
            running = await menu.RunMenu("[bold yellow]Main Menu[/]", entries);
        }
    }

    private async Task ShowServersMenu()
    {
        var running = true;
        while (running)
        {
            RenderMain(repository.Load(), "Main", "Servers");
            var entries = new List<MenuEntry>
            {
                new("Browse / Manage", 'S', executeAsync: serverMenu.SelectServer),
                new("Filter / Operate by Tag", 'T', executeAsync: tags.Show),
                new("Add Server", 'A', executeAsync: () => { AddServer(); return Task.CompletedTask; }),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            running = await menu.RunMenu("[bold yellow]Manage Servers[/]", entries);
        }
    }

    private async Task ShowGroupsMenu()
    {
        var running = true;
        while (running)
        {
            RenderMain(repository.Load(), "Main", "Groups");
            var entries = new List<MenuEntry>
            {
                new("Browse / Manage", 'S', executeAsync: groupMenu.Browse),
                new("Create Group", 'A', executeAsync: () => { CreateGroup(); return Task.CompletedTask; }),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            running = await menu.RunMenu("[bold yellow]Manage Groups[/]", entries);
        }
    }

    private void RenderMain(AppConfig config, params string[] breadcrumbs)
    {
        AnsiConsole.Clear();
        chrome.RenderBreadcrumb(breadcrumbs.Length > 0 ? breadcrumbs : ["Main"]);
        tableRenderer.RenderServerTable(config);
    }

    private void AddServer()
    {
        var host = servers.Add();
        if (host is not null)
            AnsiConsole.MarkupLine($"[green]✓[/] Server '[cyan]{Markup.Escape(host.Alias)}[/]' added.");
        else AnsiConsole.MarkupLine("[grey]Add server cancelled.[/]");
        console.WaitForKey();
    }

    private void CreateGroup()
    {
        var name = AnsiConsole.Prompt(new TextPrompt<string>("Enter [green]group name[/] (empty cancels):").AllowEmpty());
        if (string.IsNullOrWhiteSpace(name))
        {
            AnsiConsole.MarkupLine("[grey]Create group cancelled.[/]");
            return;
        }
        else
        {
            groups.Create(name.Trim());
            AnsiConsole.MarkupLine($"[green]✓[/] Group '[cyan]{Markup.Escape(name.Trim())}[/]' created.");
        }
        console.WaitForKey();
    }
}
