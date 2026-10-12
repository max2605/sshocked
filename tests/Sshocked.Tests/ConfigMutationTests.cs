using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ConfigMutationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sshocked-mutations-" + Guid.NewGuid().ToString("N"));
    private JsonConfigRepository Repository() => new(Path.Combine(_directory, "appconfig.json"));

    [Fact]
    public void ConcurrentGroupCreationAndRecentRecordingRetainBothKindsOfUpdates()
    {
        var host = new ServerHost { Id = "host", Alias = "host" };
        var repositories = Enumerable.Range(0, 4).Select(_ => Repository()).ToArray();
        repositories[0].Save(new AppConfig { Hosts = [host] });
        var groups = repositories.Select(r => new GroupManagementService(r, NullLogger<GroupManagementService>.Instance)).ToArray();
        var recents = repositories.Select(r => new RecentConnectionService(r)).ToArray();
        Parallel.For(0, 40, i =>
        {
            groups[i % 4].Create("group-" + i);
            recents[(i + 1) % 4].Record(host);
        });
        var loaded = Repository().Load();
        Assert.Equal(40, loaded.Groups.Count);
        Assert.Equal(40, Assert.Single(loaded.RecentConnections).ConnectionCount);
    }

    [Fact]
    public void DeletingAGroupDetachesHostsAndContainersWithoutRemovingRecentData()
    {
        var repository = Repository();
        var host = new ServerHost { Id = "host", Alias = "host", GroupId = "group", IsFavorite = true,
            SavedContainers = [new() { Id = "container", GroupId = "group", IsFavorite = true }] };
        repository.Save(new AppConfig { Groups = [new() { Id = "group", Name = "old" }], Hosts = [host] });
        new RecentConnectionService(Repository()).Record(host);
        var service = new GroupManagementService(repository, NullLogger<GroupManagementService>.Instance);
        service.Rename("group", "renamed");
        service.Delete("group");
        service.AssignHost("host", "group"); // Another menu still has the now-deleted ID selected.
        var loaded = Repository().Load();
        Assert.Empty(loaded.Groups);
        Assert.Empty(Assert.Single(loaded.Hosts).GroupId);
        Assert.Empty(Assert.Single(loaded.Hosts[0].SavedContainers).GroupId);
        Assert.True(loaded.Hosts[0].IsFavorite);
        Assert.True(loaded.Hosts[0].SavedContainers[0].IsFavorite);
        Assert.Single(loaded.RecentConnections);
    }

    [Fact]
    public async Task FirstStartCompletionPreservesChangesMadeWhileThePromptIsOpen()
    {
        var repository = Repository();
        var host = new ServerHost { Id = "host", Alias = "host" };
        repository.Save(new AppConfig { IsFirstStart = true, Hosts = [host] });
        var console = new StartupConsole(() =>
        {
            var otherWindow = Repository();
            new RecentConnectionService(otherWindow).Record(host);
            new RecentConnectionService(otherWindow).ToggleFavorite(host);
            new GroupManagementService(otherWindow, NullLogger<GroupManagementService>.Instance).Create("from-other-window");
        });
        var main = new MainMenu();
        var service = new AppRunner(repository, new Importer(), new Sync(), main, console, NullLogger<AppRunner>.Instance);
        await service.Run();
        var loaded = Repository().Load();
        Assert.False(loaded.IsFirstStart);
        Assert.True(Assert.Single(loaded.Hosts).IsFavorite);
        Assert.Equal(1, Assert.Single(loaded.RecentConnections).ConnectionCount);
        Assert.Equal("from-other-window", Assert.Single(loaded.Groups).Name);
        Assert.True(main.Shown);
    }

    private sealed class StartupConsole(Action onWelcome) : IConsoleWriterService
    {
        public void WriteLine(string message) { if (message.StartsWith("Welcome", StringComparison.Ordinal)) onWelcome(); }
        public void Write(string message) { }
        public string? ReadLine() => "n";
    }
    private sealed class MainMenu : IMainMenuService
    {
        public bool Shown { get; private set; }
        public Task Show() { Shown = true; return Task.CompletedTask; }
    }
    private sealed class Importer : ISshConfigImporter
    {
        public List<ServerHost> Import(string? configPath = null) => [];
        public List<ServerHost> Parse(string configContent) => [];
    }
    private sealed class Sync : ISshConfigSyncService
    {
        public void Initialize() { }
        public void RefreshMonitoring() { }
        public void Dispose() { }
        public SshConfigSyncResult Import(SyncConflictResolution? resolution = null,
            Func<ServerHost, ServerHost, SyncConflictResolution>? resolveConflict = null) => throw new NotSupportedException();
        public SshConfigExportResult Export() => throw new NotSupportedException();
        public SshConfigSyncStatus GetStatus() => throw new NotSupportedException();
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
