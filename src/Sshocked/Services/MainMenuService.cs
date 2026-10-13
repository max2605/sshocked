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
    IUiChromeService chrome, ILogger<MainMenuService> logger, ITagMenuService tags, IMacroMenuService macros,
    IMonitoringMenuService? monitoring = null, IContainerMenuService? containers = null,
    ITargetViewMenuService? views = null, IBatchMenuService? batches = null,
    CommandPaletteCatalog? paletteCatalog = null, IConfigBackupMenuService? backups = null,
    IDiagnosticMenuService? diagnostics = null, IPortForwardingMenuService? forwarding = null) : IMainMenuService
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
            var entries = new List<MenuEntry>
            {
                new("Favorites", 'F', executeAsync: quickAccess.ShowFavorites),
                new("Recent connections", 'H', executeAsync: quickAccess.ShowRecent),
                new("Quick Connect", 'C', executeAsync: () => connections.ConnectServer(repository.Load())),
                new("Connect Group", 'G', executeAsync: () => connections.ConnectGroup(repository.Load())),
                new("Run command", 'X', executeAsync: () => connections.RunGroupCommand(repository.Load())),
                new("Macros", 'M', executeAsync: macros.Show),
                new("SSH Config Sync", 'Y', executeAsync: sshConfigSyncMenu.Show),
                new("Manage Servers", 'S', executeAsync: ShowServersMenu),
                new("Manage Groups", 'R', executeAsync: ShowGroupsMenu),
                new("Settings", 'T', executeAsync: settings.Show),
                new("Exit", 'E', actionType: MenuActionType.Exit)
            };
            var additions = new List<MenuEntry>();
            if (monitoring is not null) additions.Add(new("Monitoring", 'O', executeAsync: monitoring.Show));
            if (containers is not null) additions.Add(new("Container management", 'D', executeAsync: containers.Manage));
            if (views is not null) additions.Add(new("Target views", 'V', executeAsync: views.Show));
            if (batches is not null) additions.Add(new("Batch operations", 'P', executeAsync: batches.Show));
            if (backups is not null) additions.Add(new("Configuration backup / restore", 'K', executeAsync: backups.Show));
            if (diagnostics is not null) additions.Add(new("Diagnostic bundle", 'I', executeAsync: diagnostics.Show));
            if (forwarding is not null) additions.Add(new("Port forwarding", 'L', executeAsync: forwarding.Show));
            entries.InsertRange(entries.Count - 1, additions);
            paletteCatalog?.Register(() => PaletteEntries(entries));
            running = await menu.RunMenu("[bold yellow]Main Menu[/]", entries,
                () => RenderMain(repository.Load()));
        }
    }

    private async Task ShowServersMenu()
    {
        var running = true;
        while (running)
        {
            var entries = new List<MenuEntry>
            {
                new("Browse / Manage", 'S', executeAsync: serverMenu.SelectServer),
                new("Filter / Operate by Tag", 'T', executeAsync: tags.Show),
                new("Add Server", 'A', executeAsync: () => { AddServer(); return Task.CompletedTask; }),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            running = await menu.RunMenu("[bold yellow]Manage Servers[/]", entries,
                () => RenderMain(repository.Load(), "Main", "Servers"));
        }
    }

    private async Task ShowGroupsMenu()
    {
        var running = true;
        while (running)
        {
            var entries = new List<MenuEntry>
            {
                new("Browse / Manage", 'S', executeAsync: groupMenu.Browse),
                new("Create Group", 'A', executeAsync: () => { CreateGroup(); return Task.CompletedTask; }),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            running = await menu.RunMenu("[bold yellow]Manage Groups[/]", entries,
                () => RenderMain(repository.Load(), "Main", "Groups"));
        }
    }

    private void RenderMain(AppConfig config, params string[] breadcrumbs)
    {
        AnsiConsole.Clear();
        chrome.RenderBreadcrumb(breadcrumbs.Length > 0 ? breadcrumbs : ["Main"]);
        tableRenderer.RenderServerTable(config);
    }

    private IReadOnlyList<MenuEntry> PaletteEntries(List<MenuEntry> mainActions)
    {
        var config = repository.Load();
        var result = mainActions.Where(entry => entry.ActionType == MenuActionType.Execute).ToList();
        result.AddRange(config.Hosts.Select(host => new MenuEntry("Server: " + Spectre.Console.Markup.Escape(MonitoringDisplay.Literal(host.Alias + " " + host.HostName)),
            executeAsync: () => serverMenu.ShowServerActions(repository.Load().Hosts.First(item => item.Id == host.Id)))));
        result.AddRange(config.Groups.Select(group => new MenuEntry("Group: " + Markup.Escape(MonitoringDisplay.Literal(group.Name)), executeAsync: () => groupMenu.ShowGroup(group.Id))));
        result.AddRange(config.Hosts.SelectMany(host => host.Tags.Concat(host.SavedContainers.SelectMany(container => container.Tags)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(tag => new MenuEntry("Tag: " + Markup.Escape(MonitoringDisplay.Literal(tag)), executeAsync: () => tags.ShowTag(tag))));
        return result;
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
