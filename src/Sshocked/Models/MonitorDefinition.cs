namespace Sshocked.Models;

public sealed class MonitorDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string HostId { get; set; } = "";
    public string Provider { get; set; } = "server";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 15;
    public int MaxOutputBytes { get; set; } = 1048576;
    public int FreshnessSeconds { get; set; } = 180;
    public int GraceSeconds { get; set; }
    public int ConsecutiveSamples { get; set; } = 1;
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MonitorRule> Rules { get; set; } = [];
}
