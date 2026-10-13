namespace Sshocked.Models;

public sealed class MonitorMetric
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public double? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public MonitorDataQuality Quality { get; set; } = MonitorDataQuality.Success;
    public string? Diagnostic { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset? SampledUtc { get; set; }
}
