using System.Text.Json.Serialization;

namespace Sshocked.Models;

public sealed class SessionRecordingSettings
{
    public bool Enabled { get; set; }
    public RecordingMode Mode { get; set; } = RecordingMode.OutputOnly;
    public bool CaptureInput { get; set; }
    public bool CaptureOutput { get; set; } = true;
    public bool CaptureTiming { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public long? MaxTotalSizeMb { get; set; } = 1000;
    public Dictionary<string, bool> IncludeHosts { get; set; } = [];
    public Dictionary<string, bool> ExcludeHosts { get; set; } = [];
    public bool Compress { get; set; } = true;
}

public enum RecordingMode { Disabled, OutputOnly, FullTerminal, ScriptFormat, AsciinemaCast }

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

public sealed record SessionEvent(double Time, string Data);

[JsonSerializable(typeof(SessionMetadata))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
public partial class SessionRecordingJsonContext : JsonSerializerContext;
