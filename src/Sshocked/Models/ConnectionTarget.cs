namespace Sshocked.Models;

public sealed record ConnectionTarget(ServerHost Host, ContainerModel? Container, DateTimeOffset? LastConnectedAt = null)
{
    public string Label => Container is null ? $"[S] {Host.Alias}" : $"[C] {Container.Alias ?? Container.Name} ({Host.Alias})";
}
