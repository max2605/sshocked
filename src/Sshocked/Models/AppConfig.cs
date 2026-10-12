namespace Sshocked.Models;

public sealed class AppConfig
{
    public List<ServerGroup> Groups { get; set; } = [];
    public List<ServerHost> Hosts { get; set; } = [];
    public bool IsFirstStart { get; set; } = true;
    public ConnectionStrategy DefaultConnectionStrategy { get; set; } = ConnectionStrategy.MultiTab;
    public bool TrackRecentConnections { get; set; } = true;
    public int MaxRecentConnections { get; set; } = 50;
    public List<RecentConnection> RecentConnections { get; set; } = [];
    public AuditSettings Audit { get; set; } = new();
    public SshConfigSyncSettings SshConfigSync { get; set; } = new();
}
