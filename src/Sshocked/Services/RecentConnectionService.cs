using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class RecentConnectionService(IConfigRepository repository) : IRecentConnectionService
{
    public void Record(ServerHost host, ContainerModel? container = null)
    {
        repository.Update(config =>
        {
            if (!config.TrackRecentConnections || !config.Hosts.Any(h => h.Id == host.Id)) return;
            var containerId = container?.Id ?? container?.ContainerId;
            var entry = config.RecentConnections.FirstOrDefault(r => r.HostId == host.Id && r.ContainerId == containerId);
            if (entry is null)
            {
                entry = new RecentConnection { HostId = host.Id, ContainerId = containerId };
                config.RecentConnections.Add(entry);
            }
            entry.LastConnectedAt = DateTimeOffset.UtcNow;
            entry.ConnectionCount++;
            config.RecentConnections = config.RecentConnections.OrderByDescending(r => r.LastConnectedAt)
                .Take(Math.Clamp(config.MaxRecentConnections, 1, 1000)).ToList();
        });
    }

    public void ToggleFavorite(ServerHost host, ContainerModel? container = null)
    {
        repository.Update(config =>
        {
            var saved = config.Hosts.FirstOrDefault(h => h.Id == host.Id);
            if (saved is null) return;
            if (container is null) host.IsFavorite = saved.IsFavorite = !saved.IsFavorite;
            else
            {
                var savedContainer = saved.SavedContainers.FirstOrDefault(c => (c.Id ?? c.ContainerId) == (container.Id ?? container.ContainerId));
                if (savedContainer is null) return;
                container.IsFavorite = savedContainer.IsFavorite = !savedContainer.IsFavorite;
            }
        });
    }

    public IReadOnlyList<ConnectionTarget> Favorites()
    {
        var config = repository.Load();
        return config.Hosts.SelectMany(h => h.SavedContainers.Where(c => c.IsFavorite)
                .Select(c => new ConnectionTarget(h, c))
                .Concat(h.IsFavorite ? [new ConnectionTarget(h, null)] : []))
            .OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<ConnectionTarget> Recent()
    {
        var config = repository.Load();
        var targets = new List<ConnectionTarget>();
        foreach (var recent in config.RecentConnections.OrderByDescending(r => r.LastConnectedAt))
        {
            var host = config.Hosts.FirstOrDefault(h => h.Id == recent.HostId);
            if (host is null) continue;
            var container = recent.ContainerId is null ? null : host.SavedContainers.FirstOrDefault(c => (c.Id ?? c.ContainerId) == recent.ContainerId);
            if (recent.ContainerId is not null && container is null) continue;
            targets.Add(new ConnectionTarget(host, container, recent.LastConnectedAt));
        }
        return targets;
    }

    public void Clear()
    {
        repository.Update(config => config.RecentConnections.Clear());
    }

    public void Remove(string hostId, string? containerId = null)
    {
        repository.Update(config => config.RecentConnections.RemoveAll(r => r.HostId == hostId && r.ContainerId == containerId));
    }
}
