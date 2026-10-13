using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IMonitoringProvider
{
    string Key { get; }
    string Kind { get; }
    IReadOnlyList<MonitorOption> Options { get; }
    void Validate(MonitorDefinition definition);
    Task<MonitoringObservation> Collect(ServerHost host, MonitorDefinition definition,
        MonitoringRecord? previous, CancellationToken cancellationToken = default);
}
