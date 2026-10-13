namespace Sshocked.Models;

public sealed record MonitorEvent(DateTimeOffset TimestampUtc, string Action,
    string AlarmKey, MonitorSeverity Severity);
