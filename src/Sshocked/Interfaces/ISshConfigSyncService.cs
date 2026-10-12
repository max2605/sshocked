using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISshConfigSyncService : IDisposable
{
    void Initialize();
    void RefreshMonitoring();
    SshConfigSyncResult Import(SyncConflictResolution? resolution = null,
        Func<ServerHost, ServerHost, SyncConflictResolution>? resolveConflict = null);
    SshConfigExportResult Export();
    SshConfigSyncStatus GetStatus();
}
