using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class DockerMonitoringTestsTransport(Func<string, ProcessResult> execute) : IMonitoringTransport
{
    public List<(string Command, MonitoringCommandOptions Options)> Calls { get; } = [];

    public Task<ProcessResult> Execute(ServerHost host, string command, MonitoringCommandOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((command, options));
        return Task.FromResult(execute(command));
    }
}
