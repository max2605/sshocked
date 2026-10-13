namespace Sshocked.Models;

public sealed class MonitorRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string MetricKey { get; set; } = "";
    public MonitorComparison Comparison { get; set; } = MonitorComparison.Greater;
    public double Threshold { get; set; }
    public double? ClearThreshold { get; set; }
    public MonitorSeverity Severity { get; set; } = MonitorSeverity.Warning;
    public int ConsecutiveSamples { get; set; } = 1;
    public int HoldSeconds { get; set; }
}
