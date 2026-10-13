using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ServerMenuService(IConfigRepository configRepository, IServerCrudService serverCrud,
    ITableRendererService tableRenderer, IConsoleHelperService consoleHelper, IDockerService dockerService,
    IMenuFactory menuFactory, INavigationService nav, IRecentConnectionService recents, FileTransferUi transfers,
    IUiChromeService chrome, IManagedCommandSessionService commandSession, IMacroMenuService macros) : IServerMenuService
{

    public async Task SelectServer()
    {
        var config = configRepository.Load();
        if (config.Hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No servers configured.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        nav.Push(ViewType.ServerDetail);

        try
        {
            while (true)
            {
                config = configRepository.Load();
                void RenderView()
                {
                    AnsiConsole.Clear();
                    chrome.RenderBreadcrumb("Main", "Servers");
                    tableRenderer.RenderServerTable(configRepository.Load());
                }

                var entries = config.Hosts.OrderByDescending(host => host.IsFavorite)
                    .ThenBy(host => host.Alias, StringComparer.OrdinalIgnoreCase)
                    .Select(host => new MenuEntry($"{HostVisuals.Alias(host, config.UI.Theme)} [grey]({Markup.Escape(host.HostName)})[/]",
                        executeAsync: () => ShowServerActions(host))).ToList();
                entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
                if (!await menuFactory.RunMenu("[bold yellow]Browse / Manage Servers[/]", entries, RenderView)) return;
            }
        }
        finally { nav.Pop(); }
    }

    public async Task ShowServerActions(ServerHost host)
    {
        // Each entry point owns its navigation frame, including Favorites and Recents at the root.
        nav.Push(ViewType.ServerDetail);
        try
        {
            bool shouldExit = false;

            while (!shouldExit)
            {
                var config = configRepository.Load();
                var currentHost = config.Hosts.FirstOrDefault(h => h.Id == host.Id);
                if (currentHost is null) return;
                host = currentHost;
                var palette = UiThemePalette.For(config.UI.Theme);
                void RenderView()
                {
                    AnsiConsole.Clear();
                    chrome.RenderBreadcrumb("Main", "Servers", host.Alias);
                    RenderServerHeader(host, palette);
                    RenderDockerSummary(host, palette);
                }

                var menuEntries = new List<MenuEntry>
            {
                new("Connect", 'C', executeAsync: () => consoleHelper.RunSsh(host)),
                new("Run command / record macro", 'X', executeAsync: () => commandSession.Show(host)),
                new("Run macro", 'M', executeAsync: () => macros.Run([host])),
                new(host.IsFavorite ? "Remove favorite" : "Add favorite", 'F', executeAsync: () =>
                {
                    recents.ToggleFavorite(host);
                    return Task.CompletedTask;
                }),
                new("Upload file", 'U', executeAsync: () => transfers.Upload(host)),
                new("Quick upload", 'Q', executeAsync: () => transfers.Upload(host, true)),
                new("Download file", 'O', executeAsync: () => transfers.Download(host)),
                new("Browse remote", 'R', executeAsync: () => transfers.Browse(host)),
                new("Scan Docker", 'D', executeAsync: () => ScanDockerForHost(host)),
                new("Edit", 'E', executeAsync: () =>
                {
                    serverCrud.Edit(host);
                    AnsiConsole.MarkupLine($"[green]\u2713[/] Server '[cyan]{Markup.Escape(host.Alias)}[/]' updated.");
                    consoleHelper.WaitForKey();
                    return Task.CompletedTask;
                }),
                new("Delete", 'L', executeAsync: () =>
                {
                    if (serverCrud.Delete(host))
                    {
                        AnsiConsole.MarkupLine($"[green]\u2713[/] Server '[cyan]{Markup.Escape(host.Alias)}[/]' deleted.");
                        consoleHelper.WaitForKey();
                        shouldExit = true;
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

                var shouldContinue = await menuFactory.RunMenu("[bold yellow]Server Actions[/]", menuEntries, RenderView);

                if (!shouldContinue || shouldExit) return;
            }
        }
        finally { nav.Pop(); }
    }

    private static void RenderServerHeader(ServerHost host, UiThemePalette palette)
    {
        AnsiConsole.MarkupLine($"[bold {palette.Accent}]Server: {Markup.Escape(host.Alias)}[/]");
        AnsiConsole.MarkupLine($"  Host: {Markup.Escape(host.HostName)}  User: {Markup.Escape(host.User)}  Port: {host.Port}");
        AnsiConsole.MarkupLine($"[{palette.Muted}]Tags: {Markup.Escape(string.Join(", ", host.Tags))}[/]");
        AnsiConsole.WriteLine();
    }

    private static void RenderDockerSummary(ServerHost host, UiThemePalette palette)
    {
        var dockerInfo = host.DockerInfo;
        if (dockerInfo is null)
        {
            AnsiConsole.MarkupLine($"[{palette.Muted}]Docker: Not scanned yet. Use 'Scan Docker' to discover containers.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        if (!dockerInfo.DockerAvailable)
        {
            AnsiConsole.MarkupLine($"[{palette.Warning}]Docker: Not available on this host.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var lastScan = dockerInfo.LastScannedAt.HasValue
            ? dockerInfo.LastScannedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "unknown";

        AnsiConsole.MarkupLine($"[{palette.Success}]Docker:[/] v{Markup.Escape(dockerInfo.ServerVersion ?? "?")}  {Markup.Escape(dockerInfo.OsType ?? "?")}  ({Markup.Escape(dockerInfo.Architecture ?? "?")})");
        AnsiConsole.MarkupLine($"[{palette.Success}]Containers:[/] {dockerInfo.ContainersRunning ?? 0} running / {dockerInfo.ContainersTotal ?? 0} total");
        AnsiConsole.MarkupLine($"[{palette.Success}]Compose Stacks:[/] {dockerInfo.ComposeStacks.Count}");
        AnsiConsole.MarkupLine($"[{palette.Muted}]Last scanned: {lastScan}[/]");
        AnsiConsole.WriteLine();

        if (dockerInfo.Containers.Count > 0)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[{palette.Accent}]Containers[/]")
                .AddColumns([
                    new TableColumn("ID").Centered(),
                    new TableColumn("Name").Centered(),
                    new TableColumn("Image").Centered(),
                    new TableColumn("Status").Centered(),
                    new TableColumn("Ports").Centered()
                ]);
            table.BorderStyle = Style.Parse(palette.Accent);

            foreach (var c in dockerInfo.Containers)
            {
                var shortId = c.ContainerId.Length >= 12 ? c.ContainerId[..12] : c.ContainerId;
                table.AddRow(
                    new Text(shortId),
                    new Markup($"[bold]{Markup.Escape(c.Name)}[/]"),
                    new Text(c.Image),
                    new Text(c.Status),
                    new Text(c.Ports ?? "-"));
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        if (dockerInfo.ComposeStacks.Count > 0)
        {
            var stackTable = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[{palette.Accent}]Compose Stacks[/]")
                .AddColumns([
                    new TableColumn("Name").Centered(),
                    new TableColumn("Status").Centered(),
                    new TableColumn("Config Files").Centered()
                ]);
            stackTable.BorderStyle = Style.Parse(palette.Accent);

            foreach (var s in dockerInfo.ComposeStacks)
            {
                stackTable.AddRow(
                    new Markup($"[bold]{Markup.Escape(s.Name)}[/]"),
                    new Text(s.Status),
                    new Text(s.ConfigFiles ?? "-"));
            }

            AnsiConsole.Write(stackTable);
            AnsiConsole.WriteLine();
        }
    }

    private async Task ScanDockerForHost(ServerHost host)
    {
        if (host.AuthType == AuthType.Password && host.PasswordSecretRef is null && string.IsNullOrEmpty(host.SshPassword))
        {
            AnsiConsole.MarkupLine("[yellow]Docker scan is not supported for password-authenticated hosts.[/]");
            AnsiConsole.MarkupLine("[grey]Use SSH key or agent authentication to enable Docker scanning.[/]");
            consoleHelper.WaitForKey();
            return;
        }

        if (string.IsNullOrEmpty(host.SudoPassword) && host.SudoPasswordSecretRef is null)
        {
            var needsSudo = AnsiConsole.Confirm("Does this host require [yellow]sudo[/] with a password?", false);
            if (needsSudo)
            {
                host.SudoPassword = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter [yellow]sudo password[/]:")
                        .Secret());
            }
        }

        AnsiConsole.MarkupLine("[yellow]Scanning host for Docker containers...[/]");
        AnsiConsole.MarkupLine("[grey]This may take a few seconds.[/]");

        var info = await dockerService.Scan(host);

        if (!info.DockerAvailable)
        {
            AnsiConsole.MarkupLine("[yellow]Docker is not available on this host.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]\u2713[/] Scan complete: {info.Containers.Count} container(s), {info.ComposeStacks.Count} compose stack(s) found.");
        }

        consoleHelper.WaitForKey();
    }
}


