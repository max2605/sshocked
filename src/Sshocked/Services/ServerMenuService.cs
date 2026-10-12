using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ServerMenuService(IConfigRepository configRepository, IServerCrudService serverCrud, ITableRendererService tableRenderer, IHostSelectorService hostSelector, IConsoleHelperService consoleHelper, IDockerService dockerService, IMenuFactory menuFactory, INavigationService nav, IRecentConnectionService recents, FileTransferUi transfers) : IServerMenuService
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

        while (nav.Current == ViewType.ServerDetail)
        {
            config = configRepository.Load();
            AnsiConsole.Clear();
            tableRenderer.RenderServerTable(config);

            var host = hostSelector.SelectHost(config.Hosts, "Select a [green]server[/]");
            if (host is null)
            {
                nav.Pop();
                return;
            }

            await ShowServerActions(config, host);
        }
    }

    public async Task ShowServerActions(AppConfig config, ServerHost host)
    {
        bool shouldExit = false;

        while (nav.Current == ViewType.ServerDetail && !shouldExit)
        {
            AnsiConsole.Clear();
            RenderServerHeader(host);
            RenderDockerSummary(host);

            var menuEntries = new List<MenuEntry>
            {
                new("Connect", 'C', executeAsync: () => consoleHelper.RunSsh(host)),
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

            var shouldContinue = await menuFactory.RunMenu("[bold yellow]Server Actions[/]", menuEntries);

            if (!shouldContinue || shouldExit)
            {
                return;
            }
        }
    }

    private void RenderServerHeader(ServerHost host)
    {
        AnsiConsole.MarkupLine($"[bold cyan]Server: {Markup.Escape(host.Alias)}[/]");
        AnsiConsole.MarkupLine($"  Host: {Markup.Escape(host.HostName)}  User: {Markup.Escape(host.User)}  Port: {host.Port}");
        AnsiConsole.WriteLine();
    }

    private void RenderDockerSummary(ServerHost host)
    {
        var dockerInfo = host.DockerInfo;
        if (dockerInfo is null)
        {
            AnsiConsole.MarkupLine("[grey]Docker: Not scanned yet. Use 'Scan Docker' to discover containers.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        if (!dockerInfo.DockerAvailable)
        {
            AnsiConsole.MarkupLine("[yellow]Docker: Not available on this host.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var lastScan = dockerInfo.LastScannedAt.HasValue
            ? dockerInfo.LastScannedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "unknown";

        AnsiConsole.MarkupLine($"[green]Docker:[/] v{dockerInfo.ServerVersion ?? "?"}  {dockerInfo.OsType ?? "?"}  ({dockerInfo.Architecture ?? "?"})");
        AnsiConsole.MarkupLine($"[green]Containers:[/] {dockerInfo.ContainersRunning ?? 0} running / {dockerInfo.ContainersTotal ?? 0} total");
        AnsiConsole.MarkupLine($"[green]Compose Stacks:[/] {dockerInfo.ComposeStacks.Count}");
        AnsiConsole.MarkupLine($"[grey]Last scanned: {lastScan}[/]");
        AnsiConsole.WriteLine();

        if (dockerInfo.Containers.Count > 0)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[cyan]Containers[/]")
                .AddColumns([
                    new TableColumn("ID").Centered(),
                    new TableColumn("Name").Centered(),
                    new TableColumn("Image").Centered(),
                    new TableColumn("Status").Centered(),
                    new TableColumn("Ports").Centered()
                ]);

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
                .Title("[cyan]Compose Stacks[/]")
                .AddColumns([
                    new TableColumn("Name").Centered(),
                    new TableColumn("Status").Centered(),
                    new TableColumn("Config Files").Centered()
                ]);

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
        if (host.AuthType == AuthType.Password && host.PasswordSecretRef is null)
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


