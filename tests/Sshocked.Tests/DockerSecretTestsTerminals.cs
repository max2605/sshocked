using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class DockerSecretTestsTerminals : ITerminalLauncherService
{
    public Task Launch(ServerHost host, ContainerModel? container = null) => throw new NotSupportedException();
}
