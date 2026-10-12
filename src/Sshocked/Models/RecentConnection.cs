namespace Sshocked.Models;

public sealed class RecentConnection
{
    public string HostId { get; set; } = string.Empty;
    public string? ContainerId { get; set; }
    public DateTimeOffset LastConnectedAt { get; set; }
    public int ConnectionCount { get; set; }
}
