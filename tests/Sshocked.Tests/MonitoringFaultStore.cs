using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class MonitoringFaultStore(IMonitoringStore inner) : IMonitoringStore
{
    public bool FailSave { get; set; }
    public MonitoringRecord? Load(string monitorId) => inner.Load(monitorId);
    public void Save(string monitorId, MonitoringRecord record)
    {
        if (FailSave) throw new IOException("The runtime store is unavailable.");
        inner.Save(monitorId, record);
    }
    public Task<IAsyncDisposable> Lease(string monitorId, CancellationToken cancellationToken = default) => inner.Lease(monitorId, cancellationToken);
    public void Delete(string monitorId) => inner.Delete(monitorId);
}
