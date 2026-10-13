using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Tests;

public sealed class ConfigBackupLocalRecoveryTests
{
    [Fact]
    public void LocalRecoveryRestoresExactHookEnvironmentAndPoliciesWhilePortableExportsStaySanitized()
    {
        using var fixture = new ConfigBackupTestFixture();
        var original = new AppConfig
        {
            Hosts = [ConfigBackupTestFixture.Host("original", "original")],
            TrackRecentConnections = false,
            GlobalHooks = new() { PreConnect = new() { Command = "tool --token=inline-value", Environment = new() { ["APP_MODE"] = "opaque-environment-value" } } }
        };
        fixture.Repository.Save(original);
        var result = fixture.Backups.Apply(fixture.Backups.Preview(fixture.Input(new()), ConfigBackupRestoreMode.Replace));
        var json = File.ReadAllText(result.RecoveryPath);
        Assert.Contains("opaque-environment-value", json);
        Assert.Contains("inline-value", json);
        Assert.Contains("sshocked-local-recovery", json);
        Assert.Throws<InvalidDataException>(() => fixture.Backups.Inspect(result.RecoveryPath));
        Assert.Throws<InvalidDataException>(() => fixture.Backups.Preview(result.RecoveryPath, ConfigBackupRestoreMode.Replace));

        var recovery = fixture.Backups.PreviewRecovery(result.RecoveryPath);
        Assert.True(recovery.IsLocalRecovery);
        fixture.Backups.Apply(recovery);
        var restored = fixture.Repository.Load();
        Assert.Equal("opaque-environment-value", restored.GlobalHooks.PreConnect!.Environment["APP_MODE"]);
        Assert.Equal("tool --token=inline-value", restored.GlobalHooks.PreConnect.Command);
        Assert.True(restored.GlobalHooks.PreConnect.Enabled);
        Assert.False(restored.TrackRecentConnections);
        var portable = fixture.PathFor("portable-after-recovery.json");
        fixture.Backups.Export(portable);
        Assert.DoesNotContain("opaque-environment-value", File.ReadAllText(portable));
        Assert.DoesNotContain("inline-value", File.ReadAllText(portable));
    }

    [Fact]
    public void RecoveryRejectsExternalDestinationsAndExternalCopies()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("old", "old")] });
        var preview = fixture.Backups.Preview(fixture.Input(new()), ConfigBackupRestoreMode.Replace);
        var external = fixture.PathFor("outside-recovery.json");
        Assert.Throws<ArgumentException>(() => fixture.Backups.Apply(preview, external));
        Assert.False(File.Exists(external));
        Assert.Equal("old", Assert.Single(fixture.Repository.Load().Hosts).Id);
        var result = fixture.Backups.Apply(preview);
        File.Copy(result.RecoveryPath, external);
        Assert.Throws<ArgumentException>(() => fixture.Backups.PreviewRecovery(external));
    }

    [Fact]
    public void RecoveryChecksVersionAndAccountBinding()
    {
        using var fixture = new ConfigBackupTestFixture();
        var result = fixture.Backups.Apply(fixture.Backups.Preview(fixture.Input(new()), ConfigBackupRestoreMode.Replace));
        var document = JsonSerializer.Deserialize(File.ReadAllText(result.RecoveryPath), ConfigBackupJsonContext.Default.ConfigBackupLocalRecoveryDocument)!;
        document.SchemaVersion = 2;
        File.WriteAllText(result.RecoveryPath, JsonSerializer.Serialize(document, ConfigBackupJsonContext.Default.ConfigBackupLocalRecoveryDocument));
        Assert.Throws<InvalidDataException>(() => fixture.Backups.PreviewRecovery(result.RecoveryPath));
        document.SchemaVersion = 1;
        document.UserBinding = "another-account";
        File.WriteAllText(result.RecoveryPath, JsonSerializer.Serialize(document, ConfigBackupJsonContext.Default.ConfigBackupLocalRecoveryDocument));
        Assert.Throws<InvalidDataException>(() => fixture.Backups.PreviewRecovery(result.RecoveryPath));
    }

    [Fact]
    public void RecoveryRequiresPrivatePermissions()
    {
        using var fixture = new ConfigBackupTestFixture();
        var result = fixture.Backups.Apply(fixture.Backups.Preview(fixture.Input(new()), ConfigBackupRestoreMode.Replace));
        if (OperatingSystem.IsWindows())
        {
            var file = new FileInfo(result.RecoveryPath);
            var security = file.GetAccessControl();
            Assert.True(security.AreAccessRulesProtected);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
            file.SetAccessControl(security);
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(result.RecoveryPath));
            File.SetUnixFileMode(result.RecoveryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Backups.PreviewRecovery(result.RecoveryPath));
    }

    [Fact]
    public void ConcurrentChangesInvalidateLocalRecoveryPreviewWithoutAnotherSnapshot()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("old", "old")] });
        var result = fixture.Backups.Apply(fixture.Backups.Preview(fixture.Input(new()), ConfigBackupRestoreMode.Replace));
        var recovery = fixture.Backups.PreviewRecovery(result.RecoveryPath);
        fixture.Repository.Update(config => config.TrackRecentConnections = false);
        var before = Directory.GetFiles(fixture.RecoveryDirectory).Length;
        Assert.Throws<InvalidOperationException>(() => fixture.Backups.Apply(recovery));
        Assert.False(fixture.Repository.Load().TrackRecentConnections);
        Assert.Equal(before, Directory.GetFiles(fixture.RecoveryDirectory).Length);
    }

    [Fact]
    public async Task CliLocalRecoverPreviewsBeforeExplicitApply()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("old", "old")] });
        var result = fixture.Backups.Apply(fixture.Backups.Preview(fixture.Input(new()), ConfigBackupRestoreMode.Replace));
        var handler = new Sshocked.Services.ConfigBackupCommandService(fixture.Backups, fixture.Audit);
        await handler.Execute("backup", ["recover", result.RecoveryPath]);
        Assert.Empty(fixture.Repository.Load().Hosts);
        Assert.Contains(fixture.Audit.Output, output => output.Contains("Confidential local recovery", StringComparison.Ordinal));
        await handler.Execute("backup", ["recover", result.RecoveryPath, "--apply"]);
        Assert.Equal("old", Assert.Single(fixture.Repository.Load().Hosts).Id);
    }
}
