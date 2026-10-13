using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class MenuWorkflowTargets : IGroupSessionService, IMacroMenuService, IManagedCommandSessionService
{
    public List<(string Action, string[] HostIds)> Calls { get; } = [];
    public Task Show() => Task.CompletedTask;
    public Task Show(ServerHost host) => Record("single-command", [host]);
    public Task Run(IReadOnlyList<ServerHost> hosts) => Record("macro", hosts);
    public Task ConnectServers(AppConfig config, List<ServerHost> hosts) => Record("connect", hosts);
    public Task RunServerCommands(List<ServerHost> hosts) => Record("command", hosts);
    public Task ConnectContainers(AppConfig config, List<ContainerModel> containers) => throw new InvalidOperationException("Unexpected container connection.");
    public Task RunContainerCommands(AppConfig config, List<ContainerModel> containers) => throw new InvalidOperationException("Unexpected container command.");

    private Task Record(string action, IReadOnlyList<ServerHost> hosts)
    {
        Calls.Add((action, hosts.Select(host => host.Id).ToArray()));
        return Task.CompletedTask;
    }
}
