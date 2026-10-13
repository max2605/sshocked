using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class GroupSessionService(IGroupConnectionService groupConnection,
    IContainerGroupService containerGroups, IProcessService process, IConsoleWriterService console) : IGroupSessionService
{
    public async Task ConnectServers(AppConfig config, List<ServerHost> hosts)
    {
        // Legacy sequential values remain readable, but batch connections always open independent terminals.
        AnsiConsole.MarkupLine($"[green]Connecting {hosts.Count} server(s) using multi-tab...[/]");
        await groupConnection.ConnectAllMultiTab(hosts);
    }

    public async Task ConnectContainers(AppConfig config, List<ContainerModel> containers)
    {
        AnsiConsole.MarkupLine($"[green]Exec'ing {containers.Count} container(s) using multi-tab...[/]");
        await containerGroups.ExecAllMultiTab(config, containers);
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
                var endedWithNewline = true;
                var result = await process.RunSshCommandStreaming(host, command, output =>
                {
                    console.Write(output);
                    endedWithNewline = output.EndsWith('\n');
                });
                if (!endedWithNewline) console.WriteLine(string.Empty);
                result.EnsureSuccess("Remote SSH command");
            }
            catch (Exception error)
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(error.Message)}[/]");
            }
            AnsiConsole.WriteLine();
        }
    }
}
