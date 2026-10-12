using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class GroupSessionService(IGroupConnectionService groupConnection,
    IContainerGroupService containerGroups, IProcessService process, IConsoleWriterService console) : IGroupSessionService
{
    public async Task ConnectServers(AppConfig config, List<ServerHost> hosts)
    {
        var parallel = config.DefaultConnectionStrategy == ConnectionStrategy.MultiTab;
        AnsiConsole.MarkupLine($"[green]Connecting {hosts.Count} server(s) using {(parallel ? "multi-tab" : "sequential")}...[/]");
        if (parallel) await groupConnection.ConnectAllMultiTab(hosts);
        else await groupConnection.ConnectAllSequential(hosts);
    }

    public async Task ConnectContainers(AppConfig config, List<ContainerModel> containers)
    {
        var parallel = config.DefaultConnectionStrategy == ConnectionStrategy.MultiTab;
        AnsiConsole.MarkupLine($"[green]Exec'ing {containers.Count} container(s) using {(parallel ? "multi-tab" : "sequential")}...[/]");
        if (parallel) await containerGroups.ExecAllMultiTab(config, containers);
        else await containerGroups.ExecAllSequential(config, containers);
    }

    public Task RunServerCommands(List<ServerHost> hosts) => RunCommands("servers", command => RunOnServers(hosts, command));

    public Task RunContainerCommands(AppConfig config, List<ContainerModel> containers) =>
        RunCommands("containers", command => containerGroups.RunCommandOnAll(config, containers, command));

    private async Task RunCommands(string targets, Func<string, Task> execute)
    {
        AnsiConsole.MarkupLine($"[grey]Interactive mode — type a command to run on all {targets}.[/]");
        AnsiConsole.MarkupLine("[grey]Leave empty or type [bold]exit[/] to quit.[/]");
        AnsiConsole.WriteLine();
        while (true)
        {
            console.Write("$ ");
            var command = console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(command) || command.Equals("exit", StringComparison.OrdinalIgnoreCase)) return;
            AnsiConsole.WriteLine();
            await execute(command);
        }
    }

    private async Task RunOnServers(List<ServerHost> hosts, string command)
    {
        foreach (var host in hosts)
        {
            var header = $"-- {host.Alias} ({host.User}@{host.HostName}) ";
            AnsiConsole.Markup($"[bold]{Markup.Escape(header)}[/]");
            console.WriteLine(new string('-', Math.Max(1, 60 - header.Length)));
            try
            {
                var output = await process.RunSshCommand(host, command);
                console.Write(output);
                if (!output.EndsWith('\n')) console.WriteLine(string.Empty);
            }
            catch (Exception error)
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(error.Message)}[/]");
            }
            AnsiConsole.WriteLine();
        }
    }
}
