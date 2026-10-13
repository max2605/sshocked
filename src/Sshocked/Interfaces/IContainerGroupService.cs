using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IContainerGroupService
{
    Task ExecAllSequential(AppConfig config, List<ContainerModel> containers);
    Task ExecAllMultiTab(AppConfig config, List<ContainerModel> containers);
    Task RunCommandOnAll(AppConfig config, List<ContainerModel> containers, string command);
    Task<IReadOnlyList<DockerActionResult>> RunOnTargets(IReadOnlyList<ConnectionTarget> targets, string command,
        int maxConcurrency = 4, CancellationToken ct = default) => throw new NotSupportedException();
    ServerHost? FindParentServer(AppConfig config, ContainerModel container);
}
