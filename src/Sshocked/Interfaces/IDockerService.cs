using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IDockerService
{
    Task<DockerHostInfo> Scan(ServerHost host);
    Task<DockerHostInfo> Scan(ServerHost host, CancellationToken ct) => Scan(host);
    Task<IReadOnlyList<DockerScanResult>> ScanMany(IReadOnlyList<ServerHost> hosts, int maxConcurrency = 4,
        IProgress<DockerBatchProgress>? progress = null, CancellationToken ct = default) => throw new NotSupportedException();
    Task<DockerActionResult> Operate(ServerHost host, ContainerModel container, DockerOperation operation,
        bool removalConfirmed = false, CancellationToken ct = default) => throw new NotSupportedException();
    Task<IReadOnlyList<DockerActionResult>> OperateMany(IReadOnlyList<ConnectionTarget> targets, DockerOperation operation,
        bool removalConfirmed = false, int maxConcurrency = 4, IProgress<DockerBatchProgress>? progress = null,
        CancellationToken ct = default) => throw new NotSupportedException();
    Task<DockerActionResult> Start(ServerHost host, ContainerModel container, CancellationToken ct = default) => Operate(host, container, DockerOperation.Start, ct: ct);
    Task<DockerActionResult> Stop(ServerHost host, ContainerModel container, CancellationToken ct = default) => Operate(host, container, DockerOperation.Stop, ct: ct);
    Task<DockerActionResult> Restart(ServerHost host, ContainerModel container, CancellationToken ct = default) => Operate(host, container, DockerOperation.Restart, ct: ct);
    Task<DockerActionResult> Pause(ServerHost host, ContainerModel container, CancellationToken ct = default) => Operate(host, container, DockerOperation.Pause, ct: ct);
    Task<DockerActionResult> Unpause(ServerHost host, ContainerModel container, CancellationToken ct = default) => Operate(host, container, DockerOperation.Unpause, ct: ct);
    Task<DockerActionResult> Remove(ServerHost host, ContainerModel container, bool confirmed, CancellationToken ct = default) => Operate(host, container, DockerOperation.Remove, confirmed, ct);
    Task Exec(ServerHost host, ContainerModel container);
    Task Logs(ServerHost host, ContainerModel container);
}
