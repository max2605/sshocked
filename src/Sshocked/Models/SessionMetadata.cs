namespace Sshocked.Models;

public sealed class SessionMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public string HostId { get; set; } = string.Empty;
    public string HostAlias { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public long FileSize { get; set; }
    public string RelativeFilePath { get; set; } = string.Empty;
    public string RecordingType { get; set; } = "output-only";
    public int? ExitCode { get; set; }
    public bool Compressed { get; set; }
}
