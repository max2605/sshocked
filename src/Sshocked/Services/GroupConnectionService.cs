using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class GroupConnectionService(IConsoleHelperService consoleHelper, ITerminalLauncherService terminals, ILogger<GroupConnectionService> logger) : IGroupConnectionService
{
    public async Task ConnectAllSequential(List<ServerHost> hosts)
    {
        foreach (var host in hosts)
        {
            AnsiConsole.MarkupLine($"Connecting to [cyan]{Markup.Escape(host.Alias)}[/]...");
            try { await consoleHelper.RunSsh(host); }
            catch (Exception ex) { logger.LogWarning(ex, "Connection failed for {Alias}", host.Alias); AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]"); }
        }
    }

    public async Task ConnectAllMultiTab(List<ServerHost> hosts)
    {
        var launched = 0;
        foreach (var host in hosts)
        {
            try { await terminals.Launch(host); launched++; }
            catch (Exception ex) { logger.LogWarning(ex, "Terminal launch failed for {Alias}", host.Alias); AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]"); }
        }
        AnsiConsole.MarkupLine($"[green]Launched {launched}/{hosts.Count} connection(s).[/]");
    }
}
