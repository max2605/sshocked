using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IRecentConnectionService
{
    void Record(ServerHost host, ContainerModel? container = null);
    void ToggleFavorite(ServerHost host, ContainerModel? container = null);
    IReadOnlyList<ConnectionTarget> Favorites();
    IReadOnlyList<ConnectionTarget> Recent();
    void Clear();
    void Remove(string hostId, string? containerId = null);
}
