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
