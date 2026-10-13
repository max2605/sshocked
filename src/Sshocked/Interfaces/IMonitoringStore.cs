using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IMonitoringStore
{
    MonitoringRecord? Load(string monitorId);
    void Save(string monitorId, MonitoringRecord record);
    Task<IAsyncDisposable> Lease(string monitorId, CancellationToken cancellationToken = default);
    void Delete(string monitorId);
}
