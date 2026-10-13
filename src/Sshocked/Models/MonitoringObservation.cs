namespace Sshocked.Models;

public sealed class MonitoringObservation
{
    public MonitorSnapshot Snapshot { get; set; } = new();
    public Dictionary<string, string> State { get; set; } = new(StringComparer.Ordinal);
    public List<MonitorCondition> Conditions { get; set; } = [];
}
