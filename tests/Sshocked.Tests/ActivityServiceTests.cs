using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ActivityServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-activity-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UnifiedRecordingCommandsKeepLegacyExportsAndEventQueriesCompatible()
    {
        var config = new JsonConfigRepository(Path.Combine(directory, "config.json"));
        var sessions = new SessionRepository(Path.Combine(directory, "sessions"));
        using var audit = new AuditService(config, NullLogger<AuditService>.Instance, Path.Combine(directory, "audit.jsonl"));
        var host = new ServerHost { Id = "web", Alias = "web" };
        string id;
        using (var recording = sessions.Create(host))
        { id = recording.Metadata.Id; recording.Append("playback remains available"); recording.Complete(0); }
        audit.Record(new() { Category = "connection", EventType = "connection.start", Alias = host.Alias });
        var activity = new ActivityService(audit, sessions, config);
        Assert.Single(activity.Events(new(Server: "web")));
        Assert.Equal(id, Assert.Single(activity.Recordings(server: "web")).Id);
        var console = new MacroTestDependencies();
        var legacy = new SessionCommandHandler(sessions, config);
        var unified = new ActivityCommandService(activity, new(audit, config, console), legacy, console);
        var legacyPath = Path.Combine(directory, "legacy.txt");
        var unifiedPath = Path.Combine(directory, "unified.txt");
        await legacy.Execute("sessions", ["export", id, "--format", "text", "--output", legacyPath]);
        await unified.Execute("activity", ["recordings", "export", id, "--format", "text", "--output", '"' + unifiedPath + '"']);
        Assert.Equal(File.ReadAllBytes(legacyPath), File.ReadAllBytes(unifiedPath));
        await unified.Execute("activity", ["events", "--server", "web"]);
        Assert.Contains(console.Output, output => output.Contains("connection.start"));
    }

    [Fact]
    public void EventAndPlaybackRetentionRemainIndependentWithinOneModule()
    {
        var config = new JsonConfigRepository(Path.Combine(directory, "config.json"));
        var sessions = new SessionRepository(Path.Combine(directory, "sessions"));
        using var audit = new AuditService(config, NullLogger<AuditService>.Instance, Path.Combine(directory, "audit.jsonl"));
        using (var recording = sessions.Create(new() { Alias = "web" })) { recording.Append("output"); recording.Complete(0); }
        audit.Record(new() { Category = "connection", EventType = "connection.start" });
        audit.Clear();
        var activity = new ActivityService(audit, sessions, config);
        Assert.Empty(activity.Events(new())); Assert.Single(activity.Recordings());
        config.Update(current => { current.Audit.Enabled = false; current.SessionRecording.Enabled = true; });
        Assert.Contains("Activity events: disabled", activity.Status());
        Assert.Contains("Output recordings: enabled", activity.Status());
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
