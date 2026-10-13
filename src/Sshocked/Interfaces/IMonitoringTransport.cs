using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IMonitoringTransport
{
    Task<ProcessResult> Execute(ServerHost host, string command, MonitoringCommandOptions options,
        CancellationToken cancellationToken = default);
}
