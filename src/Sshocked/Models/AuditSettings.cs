namespace Sshocked.Models;

public sealed class AuditSettings
{
    public bool Enabled { get; set; } = true;
    public bool LogConnections { get; set; } = true;
    public bool LogCommands { get; set; } = true;
    public bool LogFileTransfers { get; set; } = true;
    public bool LogConfigChanges { get; set; } = true;
    public bool LogSecretAccess { get; set; }
    public bool LogCommandText { get; set; } = true;
    public bool LogFilePaths { get; set; } = true;
    public bool IncludeLocalUsername { get; set; } = true;
    public bool IncludeLocalHostname { get; set; }
    public int RetentionDays { get; set; } = 90;
    public int MaxEntries { get; set; } = 10000;
}
