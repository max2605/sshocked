using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IMonitoringService
{
    IReadOnlyList<IMonitoringProvider> Providers { get; }
    IReadOnlyList<MonitorDefinition> Definitions();
    MonitorDefinition Find(string idOrName);
    void Save(MonitorDefinition definition, MonitorDefinition? expected = null);
    Task Delete(string id, CancellationToken cancellationToken = default);
    IReadOnlyList<MonitorSnapshot> Status(IReadOnlyList<string>? hostIds = null, string? kind = null);
    Task<IReadOnlyList<MonitorSnapshot>> Run(IReadOnlyList<string>? hostIds = null, string? kind = null, CancellationToken cancellationToken = default);
    Task<MonitorSnapshot> RunOne(string monitorId, CancellationToken cancellationToken = default);
    Task Watch(Action<IReadOnlyList<MonitorSnapshot>> publish, IReadOnlyList<string>? hostIds = null, string? kind = null, CancellationToken cancellationToken = default);
    Task Acknowledge(string monitorId, string alarmKey, CancellationToken cancellationToken = default);
}
