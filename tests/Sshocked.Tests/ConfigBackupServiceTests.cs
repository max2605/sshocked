using System.Text.Json;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ConfigBackupServiceTests
{
    [Fact]
    public void PortableExportExcludesCredentialsPrivateKeysAndHookEnvironmentsWithoutReadingOtherFiles()
    {
        using var fixture = new ConfigBackupTestFixture();
        var host = ConfigBackupTestFixture.Host("one", "web");
        host.SshPassword = "cobalt-value-123";
        host.SudoPassword = "sudo-value-456";
        host.PasswordSecretRef = new() { ProviderName = "vault", SecretId = "ssh-web", SecretType = SecretType.Password };
        host.CustomSshOptions = "--password=inline-value";
        host.Hooks = new() { PreConnect = new() { Command = "echo cobalt-value-123", Environment = new() { ["APP_ENV"] = "prod-value", ["API_KEY"] = "environment-value" } } };
        var config = new AppConfig
        {
            Hosts = [host],
            GlobalHooks = new() { PreConnect = new() { InlineScript = "-----BEGIN OPENSSH PRIVATE KEY-----\nkey-material\n-----END OPENSSH PRIVATE KEY-----" } }
        };
        Directory.CreateDirectory(fixture.PathFor("credentials"));
        File.WriteAllText(fixture.PathFor("credentials/one.dpapi"), "ciphertext-must-not-be-read");
        var service = new ConfigBackupService(new ConfigBackupTestRepository(config), fixture.Audit, fixture.RecoveryDirectory);
        var output = fixture.PathFor("portable.json");

        var document = service.Export(output);
        var json = File.ReadAllText(output);
        foreach (var value in new[] { "cobalt-value-123", "sudo-value-456", "inline-value", "prod-value", "environment-value", "PRIVATE KEY", "key-material", "ciphertext-must-not-be-read" })
            Assert.DoesNotContain(value, json);
        Assert.Contains("ssh-web", json);
        Assert.Empty(document.Configuration.Hosts[0].Hooks!.PreConnect!.Environment);
        Assert.False(document.Configuration.Hosts[0].Hooks!.PreConnect!.Enabled);
        Assert.False(document.Configuration.GlobalHooks.PreConnect!.Enabled);
        Assert.Equal("cobalt-value-123", config.Hosts[0].SshPassword);
        Assert.Equal(1, service.Inspect(output).SchemaVersion);
    }

    [Fact]
    public void MergePreviewsAddedChangedRecordsAndPreservesCurrentSettingsAndUnrelatedRecords()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("one", "before"), ConfigBackupTestFixture.Host("local", "local")], TrackRecentConnections = false });
        var input = fixture.Input(new() { Hosts = [ConfigBackupTestFixture.Host("one", "after"), ConfigBackupTestFixture.Host("added", "new")], TrackRecentConnections = true });

        var preview = fixture.Backups.Preview(input, ConfigBackupRestoreMode.Merge);
        Assert.Empty(preview.Conflicts);
        Assert.Contains(preview.Changes, change => change.Id == "one" && change.Change == "changed");
        Assert.Contains(preview.Changes, change => change.Id == "added" && change.Change == "added");
        Assert.DoesNotContain(preview.Changes, change => change.Change == "removed");
        var result = fixture.Backups.Apply(preview);
        Assert.Equal(3, fixture.Repository.Load().Hosts.Count);
        Assert.False(fixture.Repository.Load().TrackRecentConnections);
        var recovery = fixture.Backups.PreviewRecovery(result.RecoveryPath);
        Assert.True(recovery.IsLocalRecovery);
        Assert.Contains(recovery.Changes, change => change.Name == "before" && change.Change == "changed");
    }

    [Fact]
    public void SecretReferenceIdentifiersAndLiteralMetadataRemainUsable()
    {
        using var fixture = new ConfigBackupTestFixture();
        var host = ConfigBackupTestFixture.Host("host-token", "token-node");
        host.PasswordSecretRef = new() { ProviderName = "production-secrets", SecretId = "password-production", SecretType = SecretType.Password };
        fixture.Repository.Save(new()
        {
            Hosts = [host],
            SecretProviders = [new() { Name = "production-secrets", VaultUri = "https://vault.example" }]
        });
        var path = fixture.PathFor("references.json");
        fixture.Backups.Export(path);
        var config = fixture.Backups.Inspect(path).Configuration;
        Assert.Equal("production-secrets", config.Hosts[0].PasswordSecretRef!.ProviderName);
        Assert.Equal("password-production", config.Hosts[0].PasswordSecretRef!.SecretId);
        Assert.Equal("token-node", config.Hosts[0].Alias);
        Assert.Equal("production-secrets", config.SecretProviders[0].Name);
    }

    [Fact]
    public void ReplacePreviewsRemovalsAndRecoveryCanRestoreThePreviousConfiguration()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("old", "old")], TrackRecentConnections = false });
        var input = fixture.Input(new() { Hosts = [ConfigBackupTestFixture.Host("new", "new")], TrackRecentConnections = true });
        var preview = fixture.Backups.Preview(input, ConfigBackupRestoreMode.Replace);
        Assert.Contains(preview.Changes, change => change.Id == "old" && change.Change == "removed");
        var result = fixture.Backups.Apply(preview);
        Assert.Equal("new", Assert.Single(fixture.Repository.Load().Hosts).Alias);
        Assert.True(fixture.Repository.Load().TrackRecentConnections);

        fixture.Backups.Apply(fixture.Backups.PreviewRecovery(result.RecoveryPath));
        Assert.Equal("old", Assert.Single(fixture.Repository.Load().Hosts).Alias);
        Assert.False(fixture.Repository.Load().TrackRecentConnections);
    }

    [Fact]
    public void ForwardingProfilesArePreviewedAndRestoredWithoutStartingSessions()
    {
        using var fixture = new ConfigBackupTestFixture();
        var host = ConfigBackupTestFixture.Host("one", "web");
        fixture.Repository.Save(new() { Hosts = [host] });
        var profile = new ForwardingProfile { Id = "forward", Name = "database", HostId = host.Id, Kind = ForwardingKind.Local, ListenPort = 13306, TargetPort = 3306 };
        var input = fixture.Input(new() { Hosts = [host], ForwardingProfiles = [profile] });
        var preview = fixture.Backups.Preview(input, ConfigBackupRestoreMode.Merge);
        Assert.Contains(preview.Changes, change => change.Category == "forwardingProfiles" && change.Id == "forward");
        fixture.Backups.Apply(preview);
        Assert.Equal(profile.Id, Assert.Single(fixture.Repository.Load().ForwardingProfiles).Id);
        Assert.Empty(fixture.Audit.Commands);
    }

    [Fact]
    public void AliasConflictBlocksMergeAndRequiresAnExplicitReplacementChoice()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("local", "WEB")] });
        var input = fixture.Input(new() { Hosts = [ConfigBackupTestFixture.Host("imported", "web")] });
        var merge = fixture.Backups.Preview(input, ConfigBackupRestoreMode.Merge);
        Assert.Contains(merge.Conflicts, conflict => conflict.Contains("duplicate host aliases", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => fixture.Backups.Apply(merge));
        Assert.Equal("local", Assert.Single(fixture.Repository.Load().Hosts).Id);
        fixture.Backups.Apply(fixture.Backups.Preview(input, ConfigBackupRestoreMode.Replace));
        Assert.Equal("imported", Assert.Single(fixture.Repository.Load().Hosts).Id);
    }

    [Fact]
    public void ChangedConfigurationInvalidatesPreviewBeforeAnyRecoveryWriteOrMutation()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("old", "old")] });
        var preview = fixture.Backups.Preview(fixture.Input(new()), ConfigBackupRestoreMode.Replace);
        fixture.Repository.Update(config => config.Hosts.Add(ConfigBackupTestFixture.Host("concurrent", "concurrent")));
        var recovery = Path.Combine(fixture.RecoveryDirectory, "stale-recovery.json");
        Assert.Throws<InvalidOperationException>(() => fixture.Backups.Apply(preview, recovery));
        Assert.False(File.Exists(recovery));
        Assert.Equal(2, fixture.Repository.Load().Hosts.Count);
    }

    [Fact]
    public void RecoveryWriteFailureLeavesOriginalConfigurationAndExistingFileIntact()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("old", "old")] });
        var preview = fixture.Backups.Preview(fixture.Input(new()), ConfigBackupRestoreMode.Replace);
        Directory.CreateDirectory(fixture.RecoveryDirectory);
        var recovery = Path.Combine(fixture.RecoveryDirectory, "existing.json");
        File.WriteAllText(recovery, "must remain");
        Assert.Throws<IOException>(() => fixture.Backups.Apply(preview, recovery));
        Assert.Equal("must remain", File.ReadAllText(recovery));
        Assert.Equal("old", Assert.Single(fixture.Repository.Load().Hosts).Alias);
    }

    [Fact]
    public void InterruptedCommitLeavesRecoverySnapshotAndOriginalConfiguration()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("old", "old")] });
        var service = new ConfigBackupService(new ConfigBackupFailingRepository(fixture.Repository), fixture.Audit, fixture.RecoveryDirectory);
        var preview = service.Preview(fixture.Input(new()), ConfigBackupRestoreMode.Replace);
        var recovery = Path.Combine(fixture.RecoveryDirectory, "recovery-before-interruption.json");
        Assert.Throws<IOException>(() => service.Apply(preview, recovery));
        Assert.Equal("old", Assert.Single(fixture.Repository.Load().Hosts).Alias);
        Assert.True(service.PreviewRecovery(recovery).IsLocalRecovery);
        Assert.DoesNotContain(fixture.Audit.Events, entry => entry.EventType == "config.backup.restored");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{not JSON}")]
    [InlineData("{\"format\":\"sshocked-configuration\",\"schemaVersion\":2,\"configuration\":{}}")]
    [InlineData("{\"format\":\"sshocked-configuration\",\"schemaVersion\":1,\"configuration\":null}")]
    public void MalformedOrUnsupportedBackupsAreRejected(string content)
    {
        using var fixture = new ConfigBackupTestFixture();
        var path = fixture.PathFor("invalid.json");
        File.WriteAllText(path, content);
        Assert.Throws<InvalidDataException>(() => fixture.Backups.Inspect(path));
    }

    [Fact]
    public void DuplicateAliasesInsideTheBackupAreRejectedDuringInspection()
    {
        using var fixture = new ConfigBackupTestFixture();
        var input = fixture.Input(new() { Hosts = [ConfigBackupTestFixture.Host("one", "web"), ConfigBackupTestFixture.Host("two", "WEB")] });
        Assert.Throws<InvalidDataException>(() => fixture.Backups.Inspect(input));
    }

    [Fact]
    public async Task CliRequiresExplicitModeAndApplyAndCanPinTheReviewedFingerprint()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("old", "old")] });
        var input = fixture.Input(new());
        var handler = new ConfigBackupCommandService(fixture.Backups, fixture.Audit);
        await handler.Execute("backup", ["restore", input, "--mode", "replace"]);
        Assert.Equal("old", Assert.Single(fixture.Repository.Load().Hosts).Alias);
        var preview = fixture.Backups.Preview(input, ConfigBackupRestoreMode.Replace);
        fixture.Repository.Update(config => config.TrackRecentConnections = false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Execute("backup", ["restore", input, "--mode", "replace", "--apply", "--expected", preview.ExpectedFingerprint]));
        Assert.Equal("old", Assert.Single(fixture.Repository.Load().Hosts).Alias);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Execute("backup", ["restore", input, "--apply"]));
    }
}
