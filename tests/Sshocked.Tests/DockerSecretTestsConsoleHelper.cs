using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class DockerSecretTestsConsoleHelper : IConsoleHelperService
{
    public void WaitForKey() => throw new NotSupportedException();
    public Task RunSsh(ServerHost host) => throw new NotSupportedException();
    public Task RunDockerExec(ServerHost host, ContainerModel container) => throw new NotSupportedException();
    public Task RunDockerLogs(ServerHost host, ContainerModel container) => throw new NotSupportedException();
}
