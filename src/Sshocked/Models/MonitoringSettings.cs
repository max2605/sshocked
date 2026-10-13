namespace Sshocked.Models;

public sealed class MonitoringSettings
{
    public bool Enabled { get; set; }
    public int MaxConcurrency { get; set; } = 4;
    public int MaxHistoryEntries { get; set; } = 500;
    public List<MonitorDefinition> Monitors { get; set; } = [];
}
