using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class ConsoleHelperTestsRecents : IRecentConnectionService
{
    public int Count { get; private set; }

    public void Record(ServerHost host, ContainerModel? container = null) => Count++;
    public void ToggleFavorite(ServerHost host, ContainerModel? container = null)
    {
    }

    public IReadOnlyList<ConnectionTarget> Favorites() => [];
    public IReadOnlyList<ConnectionTarget> Recent() => [];
    public void Clear()
    {
    }

    public void Remove(string hostId, string? containerId = null)
    {
    }
}
