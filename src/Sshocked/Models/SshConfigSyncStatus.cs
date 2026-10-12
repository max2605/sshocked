namespace Sshocked.Models;

public sealed record SshConfigSyncStatus(bool IsWatching, bool HasPendingChanges, string? LastError, int ConfiguredHosts, int OpenSshHosts, SshConfigSyncSettings Settings);
