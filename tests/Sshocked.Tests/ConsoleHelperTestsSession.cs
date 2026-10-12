using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class ConsoleHelperTestsSession(bool fails) : ISshRunnerService, IDockerService
{
    private static void AssertHandedToProcess() => Assert.False(AnsiConsole.Console.Profile.Capabilities.Interactive);
    private Task Run()
    {
        AssertHandedToProcess();
        return fails ? Task.FromException(new IOException("process failed")) : Task.CompletedTask;
    }

    public Task Connect(ServerHost host) => Run();
    public Task Exec(ServerHost host, ContainerModel container) => Run();
    public Task Logs(ServerHost host, ContainerModel container) => Run();
    public Task<DockerHostInfo> Scan(ServerHost host) => throw new NotSupportedException();
}
