namespace Sshocked.Models;

public enum ExportStrategy { MarkedBlocks, IncludeFile }
public enum SyncConflictResolution { PromptUser, SshockedWins, OpenSshWins, KeepBoth, Skip }

public sealed class SshConfigSyncSettings
{
    public bool AutoExportOnChange { get; set; }
    public ExportStrategy Strategy { get; set; } = ExportStrategy.MarkedBlocks;
    public string? IncludeFilePath { get; set; }
    public bool ExportAllHosts { get; set; } = true;
    public List<string> ExportTags { get; set; } = [];
    public List<string> ExcludeHostIds { get; set; } = [];
    public bool ExportIdentityFile { get; set; } = true;
    public bool ExportCustomOptions { get; set; } = true;
    public bool ExportPort { get; set; } = true;
    public bool ExportUser { get; set; } = true;
    public bool AutoImportOnChange { get; set; }
    public bool ImportOnStartup { get; set; }
    public SyncConflictResolution ConflictResolution { get; set; } = SyncConflictResolution.PromptUser;
    public DateTimeOffset? LastExportUtc { get; set; }
    public DateTimeOffset? LastImportUtc { get; set; }
    public int LastExportCount { get; set; }
    public int LastImportCount { get; set; }
}

public sealed record SshConfigExportResult(int Exported, bool Changed, IReadOnlyList<string> WrittenPaths, IReadOnlyList<string> Backups);
public sealed record SshConfigSyncResult(int Added, int Updated, int Skipped, int Conflicts)
{
    public int Imported => Added + Updated;
}
public sealed record SshConfigSyncStatus(bool IsWatching, bool HasPendingChanges, string? LastError, int ConfiguredHosts,
    int OpenSshHosts, SshConfigSyncSettings Settings);
