using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class DockerService(IProcessService processService, IConfigRepository configRepository,
    ISecretProviderRegistry secrets, ILogger<DockerService> logger) : IDockerService
{
    private const string ContainerScanCommand = "docker ps -a --no-trunc --format '{\"ID\":{{json .ID}},\"Names\":{{json .Names}},\"Image\":{{json .Image}},\"Status\":{{json .Status}},\"Ports\":{{json .Ports}},\"ComposeProject\":{{json (.Label \"com.docker.compose.project\")}}}'";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(45);

    public Task<DockerHostInfo> Scan(ServerHost host) => Scan(host, CancellationToken.None);

    public async Task<DockerHostInfo> Scan(ServerHost host, CancellationToken ct)
    {
        logger.LogInformation("Scanning host {Alias} for Docker containers", host.Alias);
        var info = new DockerHostInfo();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            var result = await Run(host, "docker info --format '{{json .}}'", timeout.Token);
            if (result.ExitCode != 0) info.ScanError = DockerFailure.Message(result);
            else
            {
                DockerJsonParser.ReadHostInfo(result.StandardOutput, info);
                info.DockerAvailable = true;
                result = await Run(host, ContainerScanCommand, timeout.Token);
                if (result.ExitCode != 0) info.ScanError = DockerFailure.Message(result);
                else info.Containers = DockerJsonParser.ReadContainers(result.StandardOutput);
                if (info.ScanError is null)
                {
                    result = await Run(host, "docker compose ls --all --format json", timeout.Token);
                    if (result.ExitCode != 0) info.ScanWarning = "Docker Compose discovery is unavailable. Docker containers were scanned successfully.";
                    else info.ComposeStacks = DockerJsonParser.ReadComposeStacks(result.StandardOutput);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { info.ScanError = "Docker scan timed out after 45 seconds."; }
        catch (ProcessOutputLimitException) { info.ScanError = "Docker discovery output exceeded the capture limit. Reduce the output or check the remote shell configuration."; }
        catch (JsonException) { info.ScanError = "Docker returned invalid discovery JSON. Check the Docker CLI version and remote shell configuration."; }
        catch (Exception error)
        {
            logger.LogWarning("Docker scan failed for {Alias}: {FailureType}", host.Alias, error.GetType().Name);
            info.ScanError = "Docker scan could not authenticate or connect. Update the server's SSH/sudo credentials and check connectivity.";
        }
        info.LastScannedAt = DateTime.UtcNow;
        CacheAndSave(host, info);
        return info;
    }

    public Task<IReadOnlyList<DockerScanResult>> ScanMany(IReadOnlyList<ServerHost> hosts, int maxConcurrency = 4,
        IProgress<DockerBatchProgress>? progress = null, CancellationToken ct = default)
    {
        var targets = hosts.DistinctBy(host => host.Id).ToArray();
        var completed = 0;
        return DockerBatchRunner.Run<ServerHost, DockerScanResult>(targets, maxConcurrency, async host =>
        {
            progress?.Report(new(Volatile.Read(ref completed), targets.Length, host.Alias, null));
            DockerScanResult outcome;
            try
            {
                var info = await Scan(host, ct);
                outcome = new(host, info, info.ScanError is null ? DockerActionState.Succeeded : DockerActionState.Failed, info.ScanError);
            }
            catch (OperationCanceledException) { outcome = new(host, null, DockerActionState.Cancelled, "Scan cancelled."); }
            catch (Exception) { outcome = new(host, null, DockerActionState.Failed, "Docker scan failed. Check credentials, connectivity and configuration storage."); }
            progress?.Report(new(Interlocked.Increment(ref completed), targets.Length, host.Alias, outcome.State));
            return outcome;
        }, host => new(host, null, DockerActionState.Cancelled, "Scan cancelled before starting."), ct);
    }

    public async Task<DockerActionResult> Operate(ServerHost host, ContainerModel container, DockerOperation operation,
        bool removalConfirmed = false, CancellationToken ct = default)
    {
        var target = new ConnectionTarget(host, container);
        if (operation == DockerOperation.Remove && !removalConfirmed)
            return new(target, DockerActionState.Failed, Error: "Container removal requires explicit confirmation.");
        var verb = operation switch
        {
            DockerOperation.Start => "start",
            DockerOperation.Stop => "stop",
            DockerOperation.Restart => "restart",
            DockerOperation.Pause => "pause",
            DockerOperation.Unpause => "unpause",
            DockerOperation.Remove => "rm",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            var result = await Run(host, $"docker {verb} -- {DockerCommandSyntax.QuoteContainerName(DockerCommandSyntax.Identifier(container))}", timeout.Token);
            if (result.ExitCode != 0) return new(target, DockerActionState.Failed, result.ExitCode, Error: DockerFailure.Message(result));
            InvalidateCache(host, container, operation == DockerOperation.Remove);
            return new(target, DockerActionState.Succeeded, result.ExitCode, result.StandardOutput);
        }
        catch (OperationCanceledException)
        {
            return ct.IsCancellationRequested ? new(target, DockerActionState.Cancelled, Error: "Operation cancelled; the remote state may already have changed. Rescan before retrying.")
                : new(target, DockerActionState.Failed, Error: "Operation timed out after 45 seconds; rescan before retrying.");
        }
        catch (ProcessOutputLimitException) { return new(target, DockerActionState.Failed, Error: "Docker operation output exceeded the capture limit; the remote state may already have changed. Rescan before retrying."); }
        catch (Exception error)
        {
            logger.LogWarning("Docker operation {Operation} failed for {Alias}: {FailureType}", operation, host.Alias, error.GetType().Name);
            return new(target, DockerActionState.Failed, Error: "Docker operation could not complete. Check credentials, connectivity and configuration storage; rescan before retrying.");
        }
    }

    public Task<IReadOnlyList<DockerActionResult>> OperateMany(IReadOnlyList<ConnectionTarget> targets, DockerOperation operation,
        bool removalConfirmed = false, int maxConcurrency = 4, IProgress<DockerBatchProgress>? progress = null, CancellationToken ct = default)
    {
        if (targets.Any(target => target.Container is null)) throw new ArgumentException("Docker operations require container targets.", nameof(targets));
        var unique = targets.DistinctBy(target => (target.Host.Id, DockerCommandSyntax.Identifier(target.Container!))).ToArray();
        var completed = 0;
        return DockerBatchRunner.Run<ConnectionTarget, DockerActionResult>(unique, maxConcurrency, async target =>
        {
            progress?.Report(new(Volatile.Read(ref completed), unique.Length, target.Label, null));
            var result = await Operate(target.Host, target.Container!, operation, removalConfirmed, ct);
            progress?.Report(new(Interlocked.Increment(ref completed), unique.Length, target.Label, result.State));
            return result;
        }, target => new(target, DockerActionState.Cancelled, Error: "Operation cancelled before starting."), ct);
    }

    public Task Exec(ServerHost host, ContainerModel container) => processService.RunSshInteractive(host,
        $"sudo docker exec -it -- {DockerCommandSyntax.QuoteContainerName(container.Name)} sh");

    public Task Logs(ServerHost host, ContainerModel container) => processService.RunSshInteractive(host,
        $"sudo docker logs -f -- {DockerCommandSyntax.QuoteContainerName(container.Name)}");

    private Task<ProcessResult> Run(ServerHost host, string command, CancellationToken ct) => DockerSudo.RunResult(processService, secrets, host, command, ct);

    private void CacheAndSave(ServerHost host, DockerHostInfo info)
    {
        host.DockerInfo = info;
        configRepository.Update(config =>
        {
            var existing = config.Hosts.FirstOrDefault(item => item.Id == host.Id);
            if (existing is not null) existing.DockerInfo = info;
        });
    }

    private void InvalidateCache(ServerHost host, ContainerModel container, bool removed)
    {
        void Invalidate(ServerHost current)
        {
            lock (current)
            {
                if (current.DockerInfo is null) return;
                current.DockerInfo.LastScannedAt = null;
                if (removed) current.DockerInfo.Containers.RemoveAll(item => string.IsNullOrWhiteSpace(container.ContainerId)
                    ? item.Name == container.Name : item.ContainerId == container.ContainerId);
            }
        }
        Invalidate(host);
        configRepository.Update(config =>
        {
            var existing = config.Hosts.FirstOrDefault(item => item.Id == host.Id);
            if (existing is not null) Invalidate(existing);
        });
    }
}
