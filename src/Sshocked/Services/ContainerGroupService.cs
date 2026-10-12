using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ContainerGroupService(
    IProcessService processService,
    ITerminalLauncherService terminals,
    IConsoleHelperService consoleHelper,
    ILogger<ContainerGroupService> logger) : IContainerGroupService
{
    public ServerHost? FindParentServer(AppConfig config, ContainerModel container)
    {
        foreach (var host in config.Hosts)
        {
            if (host.SavedContainers.Any(c => c.Id == container.Id))
            {
                return host;
            }
        }
        return null;
    }

    public async Task ExecAllSequential(AppConfig config, List<ContainerModel> containers)
    {
        foreach (var container in containers)
        {
            var server = FindParentServer(config, container);
            if (server is null)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Parent server not found for container '{Markup.Escape(container.Alias ?? container.Name)}'[/]");
                continue;
            }

            AnsiConsole.MarkupLine($"Connecting to [cyan]{Markup.Escape(container.Alias ?? container.Name)}[/]...");
            await consoleHelper.RunDockerExec(server, container);
        }
    }

    public async Task ExecAllMultiTab(AppConfig config, List<ContainerModel> containers)
    {
        var launched = 0;
        foreach (var container in containers)
        {
            var server = FindParentServer(config, container);
            if (server is null) continue;
            try { await terminals.Launch(server, container); launched++; }
            catch (Exception ex) { logger.LogWarning(ex, "Container terminal launch failed"); AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]"); }
        }
        AnsiConsole.MarkupLine($"[green]Launched {launched}/{containers.Count} container connection(s).[/]");
    }

    public async Task RunCommandOnAll(AppConfig config, List<ContainerModel> containers, string command)
    {
        foreach (var container in containers)
        {
            var server = FindParentServer(config, container);
            if (server is null)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Parent server not found for container '{Markup.Escape(container.Alias ?? container.Name)}'[/]");
                AnsiConsole.WriteLine();
                continue;
            }

            EnsureSudoPassword(server);

            var header = $"-- {container.Alias ?? container.Name} ({server.Alias}) ";
            AnsiConsole.Markup($"[bold]{Markup.Escape(header)}[/]");
            AnsiConsole.WriteLine(new string('-', Math.Max(1, 60 - header.Length)));

            try
            {
                var sudo = BuildSudoPrefix(server.SudoPassword);
                var dockerCommand = $"{sudo}docker exec {container.Name} {command}";
                var output = await processService.RunSshCommand(server, dockerCommand);
                Console.Write(output);

                if (!output.EndsWith('\n'))
                {
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            }

            AnsiConsole.WriteLine();
        }

        await Task.CompletedTask;
    }

    private static string BuildSudoPrefix(string? sudoPassword)
    {
        if (string.IsNullOrEmpty(sudoPassword))
            return "sudo ";

        var escaped = sudoPassword.Replace("'", "'\\''");
        return $"echo '{escaped}' | sudo -S ";
    }

    private static void EnsureSudoPassword(ServerHost host)
    {
        if (string.IsNullOrEmpty(host.SudoPassword))
        {
            var needsSudo = AnsiConsole.Confirm("Does this host require [yellow]sudo[/] to run Docker commands?", false);
            if (needsSudo)
            {
                host.SudoPassword = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter [yellow]sudo password[/]:")
                        .Secret());
            }
        }
    }

    private static string DetectLinuxTerminal()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KONSOLE_VERSION")))
            return "konsole --new-tab -e";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GNOME_TERMINAL_SCREEN")))
            return "gnome-terminal --";

        if (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.Contains("XFCE", StringComparison.OrdinalIgnoreCase) == true)
            return "xfce4-terminal -e";

        if (Environment.GetEnvironmentVariable("DESKTOP_SESSION")?.Contains("LXDE", StringComparison.OrdinalIgnoreCase) == true)
            return "lxterminal -e";

        return "x-terminal-emulator -e";
    }
}
