namespace Sshocked.Models;

public sealed class MonitoringRecord
{
    public MonitorSnapshot Snapshot { get; set; } = new();
    public Dictionary<string, string> State { get; set; } = new(StringComparer.Ordinal);
    public List<MonitorAlarmState> Alarms { get; set; } = [];
    public List<MonitorEvent> Events { get; set; } = [];
}
