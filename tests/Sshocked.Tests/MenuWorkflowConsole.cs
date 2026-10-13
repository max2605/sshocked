using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class MenuWorkflowConsole : IConsoleHelperService
{
    public int Waits { get; private set; }
    public void WaitForKey() => Waits++;
    public Task RunSsh(ServerHost host) => throw new InvalidOperationException("Unexpected interactive SSH session.");
    public Task RunDockerExec(ServerHost host, ContainerModel container) => throw new InvalidOperationException("Unexpected Docker session.");
    public Task RunDockerLogs(ServerHost host, ContainerModel container) => throw new InvalidOperationException("Unexpected Docker logs.");
}
