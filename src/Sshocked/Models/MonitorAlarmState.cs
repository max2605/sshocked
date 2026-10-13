namespace Sshocked.Models;

public sealed class MonitorAlarmState
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public MonitorSeverity Severity { get; set; }
    public bool Active { get; set; }
    public bool Acknowledged { get; set; }
    public int ConsecutiveHits { get; set; }
    public DateTimeOffset? ConditionSinceUtc { get; set; }
    public DateTimeOffset? RaisedUtc { get; set; }
    public string? Message { get; set; }
}
