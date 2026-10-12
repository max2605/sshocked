using Microsoft.Extensions.Logging.Abstractions;

using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SshConfigSyncTests
{
    [Fact]
    public void ImportOverwritesConnectionFieldsAndRetainsSshockedMetadata()
    {
        var existing = Host("web", "old.example");
        existing.GroupId = "group"; existing.Tags = ["prod"]; existing.SavedContainers = [new() { ContainerId = "container" }];
        var config = new AppConfig { Hosts = [existing] };
        var incoming = Host("web", "new.example"); incoming.Id = "different-id";
        var result = SshConfigMerge.Merge(config, [incoming], SyncConflictResolution.OpenSshWins);
        Assert.Equal(1, result.Updated);
        Assert.Equal("new.example", existing.HostName);
        Assert.Equal("web-id", existing.Id);
        Assert.Equal("group", existing.GroupId);
        Assert.Equal(["prod"], existing.Tags);
        Assert.Equal("container", Assert.Single(existing.SavedContainers).ContainerId);
    }

    [Fact]
    public void KeepBothCreatesUniqueAliasesAndUnansweredConflictIsSkipped()
    {
        var config = new AppConfig { Hosts = [Host("web", "old"), Host("web-openssh", "other")] };
        var result = SshConfigMerge.Merge(config, [Host("web", "new")], SyncConflictResolution.KeepBoth);
        Assert.Equal(1, result.Added);
        Assert.Contains(config.Hosts, host => host.Alias == "web-openssh-2" && host.HostName == "new");
        result = SshConfigMerge.Merge(config, [Host("web", "new")], SyncConflictResolution.KeepBoth);
        Assert.Equal(0, result.Added);
        Assert.Equal(3, config.Hosts.Count);
        result = SshConfigMerge.Merge(config, [Host("web", "another")], SyncConflictResolution.PromptUser);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("old", config.Hosts.Single(h => h.Alias == "web").HostName);
    }

    [Fact]
    public void EquivalentOptionFormattingDoesNotCauseConflictOrReplaceAuthChoice()
    {
        var existing = Host("web", "web.example");
        existing.AuthType = AuthType.SshAgent;
        existing.CustomSshOptions = "-o ServerAliveInterval=60";
        var incoming = Host("web", "web.example");
        incoming.AuthType = AuthType.CustomConfig;
        incoming.CustomSshOptions = "-o \"serveraliveinterval=60\"";
        var result = SshConfigMerge.Merge(new AppConfig { Hosts = [existing] }, [incoming], SyncConflictResolution.PromptUser);
        Assert.Equal(0, result.Conflicts);
        Assert.Equal(AuthType.SshAgent, existing.AuthType);
    }

    [Fact]
    public void EndpointFallbackRequiresResolutionAndAmbiguousMatchesDoNotOverwrite()
    {
        var config = new AppConfig { Hosts = [Host("original", "same.example")] };
        var result = SshConfigMerge.Merge(config, [Host("renamed", "same.example")], SyncConflictResolution.PromptUser);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("original", Assert.Single(config.Hosts).Alias);
        config.Hosts.Add(Host("another", "same.example"));
        result = SshConfigMerge.Merge(config, [Host("new-alias", "same.example")], SyncConflictResolution.OpenSshWins);
        Assert.Equal(1, result.Added);
        Assert.Equal(3, config.Hosts.Count);
    }

    [Fact]
    public void AutoExportSuppressesRepositoryAndWatcherFeedbackLoopsAndDisposeUnsubscribes()
    {
        var repository = new SshConfigSyncTestsMemoryRepository();
        repository.Config.SshConfigSync.AutoExportOnChange = true;
        repository.Config.SshConfigSync.AutoImportOnChange = true;
        var watcher = new SshConfigSyncTestsFakeWatcher(); var exporter = new SshConfigSyncTestsFakeExporter(); var importer = new SshConfigSyncTestsFakeImporter();
        var sync = new SshConfigSyncService(repository, importer, exporter, watcher, NullLogger<SshConfigSyncService>.Instance);
        sync.Initialize();
        repository.Update(config => config.Hosts.Add(Host("added", "new.example")));
        Assert.Equal(1, exporter.Calls);
        watcher.Raise();
        Assert.Equal(0, importer.Calls);
        sync.Dispose();
        Assert.False(watcher.IsWatching);
        repository.Update(config => config.Hosts.Add(Host("later", "later.example")));
        Assert.Equal(1, exporter.Calls);
    }

    private static ServerHost Host(string alias, string hostname) => new() { Id = alias + "-id", Alias = alias, HostName = hostname, User = "admin" };
}
