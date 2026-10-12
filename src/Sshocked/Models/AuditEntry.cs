namespace Sshocked.Models;

public sealed class AuditEntry
{
    public Guid EntryId { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? HostId { get; set; }
    public string? Alias { get; set; }
    public string? Action { get; set; }
    public string? Username { get; set; }
    public string? Hostname { get; set; }
    public bool Success { get; set; } = true;
    public int? ExitCode { get; set; }
    public double? DurationSeconds { get; set; }
    public string? ErrorMessage { get; set; }
    public string AppVersion { get; set; } = typeof(AuditEntry).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}

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

public sealed record AuditQuery(int Last = 20, string? Type = null, string? Server = null,
    DateTimeOffset? Since = null, DateTimeOffset? Until = null);
