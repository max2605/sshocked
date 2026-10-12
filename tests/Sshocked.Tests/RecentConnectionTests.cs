using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class RecentConnectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sshocked-tests-" + Guid.NewGuid().ToString("N"));
    private JsonConfigRepository CreateRepository() => new(Path.Combine(_directory, "appconfig.json"));

    [Fact]
    public void FavoritesAndRecentsSurviveReloadAndIgnoreDeletedTargets()
    {
        var repository = CreateRepository();
        var host = new ServerHost { Id = "server", Alias = "web", SavedContainers = [new() { Id = "container", Name = "nginx" }] };
        repository.Save(new AppConfig { Hosts = [host] });
        var service = new RecentConnectionService(repository);
        service.ToggleFavorite(host);
        service.ToggleFavorite(host, host.SavedContainers[0]);
        service.Record(host);
        service.Record(host);
        service.Record(host, host.SavedContainers[0]);
        var reloaded = new RecentConnectionService(CreateRepository());
        Assert.Equal(2, reloaded.Favorites().Count);
        Assert.Equal(2, reloaded.Recent().Count);
        Assert.Equal(2, repository.Load().RecentConnections.Single(r => r.ContainerId is null).ConnectionCount);
        repository.Update(c => c.Hosts.Clear());
        Assert.Empty(reloaded.Favorites());
        Assert.Empty(reloaded.Recent());
    }

    [Fact]
    public void DisabledTrackingAndRetentionAreApplied()
    {
        var repository = CreateRepository();
        var first = new ServerHost { Id = "one" };
        var second = new ServerHost { Id = "two" };
        repository.Save(new AppConfig { Hosts = [first, second], MaxRecentConnections = 1 });
        var service = new RecentConnectionService(repository);
        service.Record(first);
        service.Record(second);
        Assert.Equal("two", Assert.Single(service.Recent()).Host.Id);
        repository.Update(c => c.TrackRecentConnections = false);
        service.Record(first);
        Assert.Equal("two", Assert.Single(service.Recent()).Host.Id);
        service.Clear();
        Assert.Empty(service.Recent());
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
