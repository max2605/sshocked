using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class ProcessMonitoringTestTransport : IMonitoringTransport
{
    public ProcessResult Result { get; set; } = new(0, "", "");
    public string Command { get; private set; } = "";
    public MonitoringCommandOptions? Options { get; private set; }

    public Task<ProcessResult> Execute(ServerHost host, string command, MonitoringCommandOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Command = command;
        Options = options;
        return Task.FromResult(Result);
    }
}
