namespace Sshocked.Models;

public sealed class RecentConnection
{
    public string HostId { get; set; } = string.Empty;
    public string? ContainerId { get; set; }
    public DateTimeOffset LastConnectedAt { get; set; }
    public int ConnectionCount { get; set; }
}

public sealed record ConnectionTarget(ServerHost Host, ContainerModel? Container, DateTimeOffset? LastConnectedAt = null)
{
    public string Label => Container is null ? $"[S] {Host.Alias}" : $"[C] {Container.Alias ?? Container.Name} ({Host.Alias})";
}
