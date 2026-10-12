using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class ConfigMutationTestsSync : ISshConfigSyncService
{
    public void Initialize()
    {
    }

    public void RefreshMonitoring()
    {
    }

    public void Dispose()
    {
    }

    public SshConfigSyncResult Import(SyncConflictResolution? resolution = null, Func<ServerHost, ServerHost, SyncConflictResolution>? resolveConflict = null) => throw new NotSupportedException();
    public SshConfigExportResult Export() => throw new NotSupportedException();
    public SshConfigSyncStatus GetStatus() => throw new NotSupportedException();
}
