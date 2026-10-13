using System.Text.Json;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class DiagnosticServiceTests
{
    private readonly DiagnosticTestDependencies dependencies = new();
    private DiagnosticService Service() => new(dependencies, dependencies, dependencies, dependencies, dependencies);

    [Fact]
    public async Task DefaultReportContainsSafeMetadataAndFailuresWithoutSensitiveContentOrProbes()
    {
        var host = ConfigBackupTestFixture.Host("host-id", "[red]literal[/]");
        host.SshPassword = "cobalt-value-123";
        host.SudoPassword = "sudo-value-456";
        host.IdentityFile = "C:\\private\\key.pem";
        dependencies.Config.Hosts = [host];
        dependencies.Tools = [new("ssh", true, "OpenSSH_9.8 cobalt-value-123", "C:\\private\\ssh.exe")];
        dependencies.Entries.Add(new()
        {
            HostId = host.Id,
            Category = "command",
            EventType = "command.failed",
            Success = false,
            Action = "tool --token=token-value /private/script",
            ErrorMessage = "cobalt-value-123 C:\\private\\key.pem"
        });
        dependencies.Entries.Add(new() { HostId = "other", Category = "command", Success = false, ErrorMessage = "other-host-value" });

        var report = await Service().Create(host.Id, new());
        var json = JsonSerializer.Serialize(report, DiagnosticJsonContext.Default.DiagnosticReport);
        foreach (var value in new[] { "cobalt-value-123", "sudo-value-456", "token-value", "private", "other-host-value" }) Assert.DoesNotContain(value, json);
        Assert.Equal("[red]literal[/]", report.Host!.Alias);
        Assert.False(report.IncludesPaths);
        Assert.False(report.IncludesCommandText);
        Assert.Null(report.Host.IdentityPath);
        Assert.Null(Assert.Single(report.Failures).Action);
        Assert.Equal(host.Id, dependencies.QueriedHost);
        Assert.Equal(0, dependencies.ProbeCalls);
    }

    [Fact]
    public async Task ExplicitInclusionCannotOverrideAuditPrivacySettings()
    {
        dependencies.Config.Hosts = [ConfigBackupTestFixture.Host("one", "web")];
        dependencies.Config.Hosts[0].IdentityFile = "/private/key";
        dependencies.Config.Audit.LogCommandText = false;
        dependencies.Config.Audit.LogFilePaths = false;
        dependencies.Entries.Add(new() { HostId = "one", Category = "command", Success = false, Action = "cat /private/script", ErrorMessage = "/private/key" });
        var report = await Service().Create("one", new(IncludePaths: true, IncludeCommandText: true));
        Assert.False(report.IncludesPaths);
        Assert.False(report.IncludesCommandText);
        Assert.Contains(report.Checks, check => check.Name == "privacy");
        Assert.Null(report.Failures[0].Action);
        Assert.Null(report.Host!.IdentityPath);
    }

    [Fact]
    public async Task ExplicitCommandTextStillRedactsPasswordsTokensKeysAndPaths()
    {
        var host = ConfigBackupTestFixture.Host("one", "web");
        host.SshPassword = "cobalt-value-123";
        dependencies.Config.Hosts = [host];
        dependencies.Entries.Add(new()
        {
            HostId = "one",
            Category = "command",
            Success = false,
            Action = "tool --token=token-value cobalt-value-123 \"C:\\Program Files\\private\\script.ps1\" host:/private/path",
            ErrorMessage = "-----BEGIN RSA PRIVATE KEY-----\nbase64-key-data\n-----END RSA PRIVATE KEY-----"
        });
        var report = await Service().Create("one", new(IncludeCommandText: true));
        var json = JsonSerializer.Serialize(report, DiagnosticJsonContext.Default.DiagnosticReport);
        foreach (var value in new[] { "token-value", "cobalt-value-123", "Program Files", "script.ps1", "/private/path", "base64-key-data", "PRIVATE KEY" })
            Assert.DoesNotContain(value, json);
        Assert.Contains("[REDACTED]", report.Failures[0].Action);
        Assert.Contains("[path omitted]", report.Failures[0].Action);
    }

    [Fact]
    public async Task PathsAreIncludedOnlyExplicitlyAndNoKeyContentsAreRead()
    {
        using var fixture = new ConfigBackupTestFixture();
        var key = fixture.PathFor("private-key");
        File.WriteAllText(key, "private-key-content-must-not-be-collected");
        var host = ConfigBackupTestFixture.Host("one", "web");
        host.IdentityFile = key;
        dependencies.Config.Hosts = [host];
        dependencies.Tools = [new("ssh", true, "OpenSSH_9.8", fixture.PathFor("ssh.exe"))];
        var report = await Service().Create("one", new(IncludePaths: true));
        Assert.Equal(key, report.Host!.IdentityPath);
        Assert.NotNull(report.Tools[0].Path);
        Assert.Contains(report.Checks, check => check.Name == "identity-file" && check.Status == "ok");
        Assert.DoesNotContain("private-key-content-must-not-be-collected", JsonSerializer.Serialize(report, DiagnosticJsonContext.Default.DiagnosticReport));
    }

    [Fact]
    public async Task ExplicitProbeAndMissingToolsProduceActionablePrerequisiteFailures()
    {
        dependencies.Config.Hosts = [ConfigBackupTestFixture.Host("one", "web")];
        dependencies.Tools = [new("ssh", false, null, null, "Not found on PATH.")];
        var report = await Service().Create("web", new(Probe: true));
        Assert.Equal(1, dependencies.ProbeCalls);
        Assert.Contains(report.Checks, check => check.Name == "required-tool" && check.Status == "failed");
        Assert.Contains(report.Checks, check => check.Name == "route" && check.Status == "failed");
    }

    [Fact]
    public async Task AmbiguousHostAliasesAreRejectedWithoutReportGeneration()
    {
        dependencies.Config.Hosts = [ConfigBackupTestFixture.Host("one", "web"), ConfigBackupTestFixture.Host("two", "WEB")];
        await Assert.ThrowsAsync<ArgumentException>(() => Service().Create("web", new()));
        Assert.Equal(0, dependencies.ProbeCalls);
    }

    [Fact]
    public async Task ExportIsLocalJsonAndDoesNotOverwriteAnExistingReport()
    {
        using var fixture = new ConfigBackupTestFixture();
        var service = Service();
        var report = await service.Create(null, new());
        var path = fixture.PathFor("diagnostic.json");
        service.Export(report, path);
        Assert.Equal(1, JsonSerializer.Deserialize(File.ReadAllText(path), DiagnosticJsonContext.Default.DiagnosticReport)!.SchemaVersion);
        Assert.Throws<IOException>(() => service.Export(report, path));
    }

    [Fact]
    public async Task CliPreviewDoesNotSaveAndExportRequiresAnExplicitDestination()
    {
        using var fixture = new ConfigBackupTestFixture();
        var handler = new DiagnosticCommandService(Service(), dependencies);
        await handler.Execute("diagnostics", ["preview"]);
        Assert.Contains(dependencies.Output, output => output.Contains("No upload", StringComparison.Ordinal));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Execute("diagnostics", ["export"]));
        var path = fixture.PathFor("cli-report.json");
        await handler.Execute("diagnostics", ["export", "--output", path]);
        Assert.True(File.Exists(path));
    }
}
