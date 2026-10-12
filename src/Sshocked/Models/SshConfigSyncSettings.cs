namespace Sshocked.Models;

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
