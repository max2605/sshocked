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
