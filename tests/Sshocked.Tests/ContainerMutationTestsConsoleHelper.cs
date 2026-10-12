using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class ContainerMutationTestsConsoleHelper : IConsoleHelperService
{
    public void WaitForKey() => throw new InvalidOperationException("Container editing unexpectedly displayed a conflict.");
    public Task RunSsh(ServerHost host) => throw new NotSupportedException();
    public Task RunDockerExec(ServerHost host, ContainerModel container) => throw new NotSupportedException();
    public Task RunDockerLogs(ServerHost host, ContainerModel container) => throw new NotSupportedException();
}
