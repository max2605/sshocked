using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ContainerGroupService(
    IProcessService processService,
    ITerminalLauncherService terminals,
    IConsoleHelperService consoleHelper,
    ISecretProviderRegistry secrets,
    ILogger<ContainerGroupService> logger) : IContainerGroupService
{
    public ServerHost? FindParentServer(AppConfig config, ContainerModel container)
    {
        foreach (var host in config.Hosts)
        {
            if (host.SavedContainers.Any(c => ReferenceEquals(c, container) || (!string.IsNullOrEmpty(container.Id) && c.Id == container.Id)))
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
        var targets = new List<ConnectionTarget>();
        foreach (var container in containers)
        {
            var server = FindParentServer(config, container);
            if (server is null)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Parent server not found for container '{Markup.Escape(container.Alias ?? container.Name)}'[/]");
                AnsiConsole.WriteLine();
                continue;
            }

            targets.Add(new(server, container));
        }
        foreach (var result in await RunOnTargets(targets, command))
        {
            AnsiConsole.WriteLine($"-- {result.Target.Label} ({result.State})");
            AnsiConsole.WriteLine(result.Error ?? result.Output);
        }
    }

    public Task<IReadOnlyList<DockerActionResult>> RunOnTargets(IReadOnlyList<ConnectionTarget> targets, string command,
        int maxConcurrency = 4, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Contains('\0')) throw new ArgumentException("Commands cannot contain NUL.", nameof(command));
        var unique = targets.DistinctBy(target => (target.Host.Id, target.Container is null ? null : DockerCommandSyntax.Identifier(target.Container))).ToArray();
        return DockerBatchRunner.Run<ConnectionTarget, DockerActionResult>(unique, maxConcurrency, async target =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                var result = target.Container is null ? await processService.RunSshCommandResultNonInteractive(target.Host, command, timeout.Token)
                    : await DockerSudo.RunResult(processService, secrets, target.Host,
                        $"docker exec -- {DockerCommandSyntax.QuoteContainerName(DockerCommandSyntax.Identifier(target.Container))} sh -lc {DockerCommandSyntax.QuoteCommand(command)}", timeout.Token);
                var output = SshCredentialRedactor.RedactCommand(result.StandardOutput + result.StandardError, target.Host);
                return result.ExitCode == 0 ? new(target, DockerActionState.Succeeded, result.ExitCode, output)
                    : new(target, DockerActionState.Failed, result.ExitCode, output, target.Container is null ? $"Remote command failed (exit code {result.ExitCode})." : DockerFailure.Message(result));
            }
            catch (OperationCanceledException) { return new(target, ct.IsCancellationRequested ? DockerActionState.Cancelled : DockerActionState.Failed, Error: "Command cancelled or timed out; check the target before retrying."); }
            catch (ProcessOutputLimitException) { return new(target, DockerActionState.Failed, Error: "Command output exceeded the capture limit; check the target before retrying."); }
            catch (Exception error)
            {
                logger.LogWarning("Target command failed for {Alias}: {FailureType}", target.Host.Alias, error.GetType().Name);
                return new(target, DockerActionState.Failed, Error: "Remote command could not authenticate or connect. Check the server credentials and connectivity.");
            }
        }, target => new(target, DockerActionState.Cancelled, Error: "Command cancelled before starting."), ct);
    }

}
