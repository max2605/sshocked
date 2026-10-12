using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class AuditServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sshocked-audit-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void QueriesFilterMachineReadableEntriesAndHonorPrivacy()
    {
        var config = new JsonConfigRepository(Path.Combine(_directory, "appconfig.json"));
        using var audit = new AuditService(config, NullLogger<AuditService>.Instance, Path.Combine(_directory, "audit.jsonl"));
        audit.Record(new() { Category = "command", EventType = "command.exec", Alias = "web", Action = "mysql --password secret", ExitCode = 7, Success = false });
        audit.Record(new() { Category = "connection", EventType = "connection.start", Alias = "db" });
        Assert.Single(audit.Query(new(Type: "command", Server: "web")));
        Assert.DoesNotContain("secret", File.ReadAllText(Path.Combine(_directory, "audit.jsonl")));
        config.Update(c => c.Audit.LogCommandText = false);
        audit.Record(new() { Category = "command", EventType = "command.exec", Action = "sensitive command" });
        Assert.Equal("[command omitted]", audit.Query(new()).First().Action);
        Assert.Single(audit.Query(new(Since: DateTimeOffset.UtcNow.AddMinutes(-1), Server: "db")));
        audit.Clear();
        Assert.Empty(audit.Query(new()));
    }

    [Theory]
    [InlineData("mysql -pVerySensitive", "VerySensitive")]
    [InlineData("echo 'VerySensitive' | sudo -S docker ps", "VerySensitive")]
    [InlineData("curl -H 'Authorization: Bearer VerySensitive'", "VerySensitive")]
    [InlineData("tool --password=VerySensitive --token VerySensitive", "VerySensitive")]
    public void CommonSecretFormsAreRedacted(string command, string secret) => Assert.DoesNotContain(secret, AuditService.Redact(command));

    [Fact]
    public void RetentionAndConfigEventsPreserveOnlyAllowedEntries()
    {
        var config = new JsonConfigRepository(Path.Combine(_directory, "appconfig.json"));
        config.Save(new AppConfig { Audit = new() { RetentionDays = 1, MaxEntries = 2 } });
        using var audit = new AuditService(config, NullLogger<AuditService>.Instance, Path.Combine(_directory, "audit.jsonl"));
        audit.Record(new() { Category = "connection", Timestamp = DateTimeOffset.UtcNow.AddDays(-2) });
        Assert.Empty(audit.Query(new()));
        config.Update(c => c.Hosts.Add(new() { Id = "new", Alias = "web", SshPassword = "not-saved" }));
        Assert.Equal("config.create", Assert.Single(audit.Query(new())).EventType);
        Assert.DoesNotContain("not-saved", File.ReadAllText(Path.Combine(_directory, "audit.jsonl")));
        config.Update(c => c.Audit.Enabled = false);
        audit.Record(new() { Category = "command", EventType = "disabled" });
        Assert.DoesNotContain(audit.Query(new()), e => e.EventType == "disabled");
    }

    [Fact]
    public async Task ConcurrentWritersKeepAllEventsAndEnforceExactRetention()
    {
        var path = Path.Combine(_directory, "audit.jsonl");
        var config = new JsonConfigRepository(Path.Combine(_directory, "appconfig.json"));
        config.Update(c => c.Audit.MaxEntries = 100);
        var writers = Enumerable.Range(0, 4).Select(_ => new AuditService(config, NullLogger<AuditService>.Instance, path)).ToArray();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() => writers[i % writers.Length].Record(
                new() { Category = "connection", EventType = "parallel", Action = i.ToString() }))));
            Assert.Equal(40, writers[0].Query(new(Last: 100)).Count);
            Assert.Equal(40, writers[0].Query(new(Last: 100)).Select(e => e.EntryId).Distinct().Count());
            config.Update(c => c.Audit.MaxEntries = 3);
            writers[0].Record(new() { Category = "connection", EventType = "last" });
            Assert.Equal(3, File.ReadLines(path).Count());
            Assert.Equal("last", writers[0].Query(new()).First().EventType);
        }
        finally { foreach (var writer in writers) writer.Dispose(); }
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
