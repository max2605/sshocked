namespace Sshocked.Models;

public sealed class MonitorSnapshot
{
    public string MonitorId { get; set; } = "";
    public string HostId { get; set; } = "";
    public string Provider { get; set; } = "";
    public DateTimeOffset ObservedUtc { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public MonitorDataQuality Quality { get; set; } = MonitorDataQuality.Unchecked;
    public MonitorSeverity Severity { get; set; } = MonitorSeverity.Unknown;
    public string? Message { get; set; }
    public List<MonitorMetric> Metrics { get; set; } = [];
}
