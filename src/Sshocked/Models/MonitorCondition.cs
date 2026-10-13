namespace Sshocked.Models;

public sealed record MonitorCondition(string Key, string Name, bool? Triggered,
    MonitorSeverity Severity = MonitorSeverity.Warning, string? Message = null,
    int ConsecutiveSamples = 1, int HoldSeconds = 0);
