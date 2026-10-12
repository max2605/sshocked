using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SshRunnerLifecycleTests
{
    [Fact]
    public async Task SuccessfulSessionRunsPhasesInOrderAndRecordsOneOutcome()
    {
        var fixture = new Fixture();
        await fixture.Runner.Connect(fixture.Host);
        Assert.Equal(new[]
        {
            "audit:connection.start", "PreConnect", "remote-wrapper", "ssh-start", "PostConnect",
            "ssh-exit", "PreDisconnect", "PostDisconnect", "recent", "audit:connection.end"
        }, fixture.Events);
        Assert.Equal("remote wrapper", fixture.Process.InitialCommand);
        Assert.False(fixture.Process.AuditCommand);
        Assert.Equal("Production", fixture.Hooks.Contexts[HookPhase.PreConnect].GroupName);
        var cleanup = fixture.Hooks.Contexts[HookPhase.PostDisconnect];
        Assert.Equal(0, cleanup.ExitCode);
        Assert.True(cleanup.DurationSeconds >= 0);
        Assert.Equal(1, fixture.Recents.Count);
        Assert.True(Assert.Single(fixture.Audit.Entries, entry => entry.EventType == "connection.end").Success);
    }

    [Fact]
    public async Task PreConnectFailurePreventsSpawnButRunsFinalCleanupAndOneAuditEnd()
    {
        var fixture = new Fixture();
        fixture.Hooks.Results[HookPhase.PreConnect] = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runner.Connect(fixture.Host));
        Assert.Contains("pre-connect", error.Message);
        Assert.DoesNotContain("ssh-start", fixture.Events);
        Assert.DoesNotContain("remote-wrapper", fixture.Events);
        Assert.DoesNotContain("PreDisconnect", fixture.Events);
        Assert.Contains("PostDisconnect", fixture.Events);
        Assert.Equal(0, fixture.Recents.Count);
        var outcome = Assert.Single(fixture.Audit.Entries, entry => entry.EventType == "connection.end");
        Assert.False(outcome.Success);
        Assert.Null(outcome.ExitCode);
    }

    [Fact]
    public async Task ProcessLaunchFailureSkipsStartedCleanupAndRecordsFailureOnce()
    {
        var fixture = new Fixture();
        fixture.Process.LaunchFailure = new IOException("SSH executable missing");
        await Assert.ThrowsAsync<IOException>(() => fixture.Runner.Connect(fixture.Host));
        Assert.DoesNotContain("PostConnect", fixture.Events);
        Assert.DoesNotContain("PreDisconnect", fixture.Events);
        Assert.Contains("PostDisconnect", fixture.Events);
        Assert.Single(fixture.Audit.Entries, entry => entry.EventType == "connection.end");
        Assert.Equal(0, fixture.Recents.Count);
    }

    [Fact]
    public async Task PostConnectFailureAbortsSessionAndStillRunsDisconnectCleanup()
    {
        var fixture = new Fixture();
        fixture.Hooks.Results[HookPhase.PostConnect] = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runner.Connect(fixture.Host));
        Assert.Contains("post-connect", error.Message);
        Assert.Contains("ssh-aborted", fixture.Events);
        Assert.Contains("PreDisconnect", fixture.Events);
        Assert.Contains("PostDisconnect", fixture.Events);
        Assert.False(Assert.Single(fixture.Audit.Entries, entry => entry.EventType == "connection.end").Success);
    }

    [Fact]
    public async Task SshFailureSuppliesExitCodeToCleanupAndPreservesOriginalFailure()
    {
        var fixture = new Fixture();
        fixture.Process.ExitCode = 255;
        fixture.Hooks.Failures[HookPhase.PreDisconnect] = new InvalidOperationException("cleanup also failed");
        var error = await Assert.ThrowsAsync<ProcessExecutionException>(() => fixture.Runner.Connect(fixture.Host));
        Assert.Equal(255, error.ExitCode);
        Assert.Equal(255, fixture.Hooks.Contexts[HookPhase.PreDisconnect].ExitCode);
        Assert.Contains("PostDisconnect", fixture.Events);
        var outcome = Assert.Single(fixture.Audit.Entries, entry => entry.EventType == "connection.end");
        Assert.False(outcome.Success);
        Assert.Equal(255, outcome.ExitCode);
        Assert.Equal(0, fixture.Recents.Count);
    }

    [Theory]
    [InlineData(HookPhase.PreDisconnect)]
    [InlineData(HookPhase.PostDisconnect)]
    public async Task CleanupFailureAfterSuccessfulSshIsReportedWithoutSkippingOtherCleanup(HookPhase phase)
    {
        var fixture = new Fixture();
        fixture.Hooks.Results[phase] = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runner.Connect(fixture.Host));
        Assert.Contains("PreDisconnect", fixture.Events);
        Assert.Contains("PostDisconnect", fixture.Events);
        var outcome = Assert.Single(fixture.Audit.Entries, entry => entry.EventType == "connection.end");
        Assert.False(outcome.Success);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public async Task PostConnectFailureIsPreservedWhenBothCleanupHooksThrow()
    {
        var fixture = new Fixture();
        fixture.Hooks.Results[HookPhase.PostConnect] = false;
        fixture.Hooks.Failures[HookPhase.PreDisconnect] = new IOException("pre cleanup");
        fixture.Hooks.Failures[HookPhase.PostDisconnect] = new IOException("post cleanup");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runner.Connect(fixture.Host));
        Assert.Contains("post-connect", error.Message);
        Assert.Contains("PreDisconnect", fixture.Events);
        Assert.Contains("PostDisconnect", fixture.Events);
        Assert.Single(fixture.Audit.Entries, entry => entry.EventType == "connection.end");
    }

    private sealed class Fixture
    {
        public List<string> Events { get; } = [];
        public ServerHost Host { get; } = new() { Id = "host", Alias = "web", HostName = "example", User = "alice", GroupId = "prod" };
        public Processes Process { get; }
        public Hooks Hooks { get; }
        public Audit Audit { get; }
        public Recents Recents { get; }
        public SshRunnerService Runner { get; }
        public Fixture()
        {
            Process = new(Events); Hooks = new(Events); Audit = new(Events); Recents = new(Events);
            Runner = new(Process, Recents, Audit, Hooks, new Config(), NullLogger<SshRunnerService>.Instance);
        }
    }

    private sealed class Processes(List<string> events) : IProcessService
    {
        public int ExitCode { get; set; }
        public Exception? LaunchFailure { get; set; }
        public string? InitialCommand { get; private set; }
        public bool AuditCommand { get; private set; } = true;
        public Task RunSsh(ServerHost host) => throw new NotSupportedException();
        public Task<string> RunSshCommand(ServerHost host, string command) => throw new NotSupportedException();
        public Task RunSshInteractive(ServerHost host, string command) => throw new NotSupportedException();
        public async Task<int> RunSshSession(ServerHost host, string? initialCommand = null, CancellationToken ct = default,
            Func<Task>? onStarted = null, bool auditCommand = true)
        {
            if (LaunchFailure is not null) throw LaunchFailure;
            InitialCommand = initialCommand;
            AuditCommand = auditCommand;
            events.Add("ssh-start");
            try { if (onStarted is not null) await onStarted(); }
            catch { events.Add("ssh-aborted"); throw; }
            events.Add("ssh-exit");
            return ExitCode;
        }
    }

    private sealed class Hooks(List<string> events) : IHookExecutorService
    {
        public Dictionary<HookPhase, bool> Results { get; } = [];
        public Dictionary<HookPhase, Exception> Failures { get; } = [];
        public Dictionary<HookPhase, HookContext> Contexts { get; } = [];
        public Task<bool> RunLocalHooks(HookPhase phase, ServerHost host, GlobalHooks? globalHooks, HookContext context, CancellationToken cancellationToken = default)
        {
            events.Add(phase.ToString()); Contexts[phase] = context;
            if (Failures.TryGetValue(phase, out var failure)) throw failure;
            return Task.FromResult(Results.GetValueOrDefault(phase, true));
        }
        public string? BuildRemoteSessionCommand(ServerHost host, GlobalHooks? globalHooks, HookContext context)
        { events.Add("remote-wrapper"); return "remote wrapper"; }
    }

    private sealed class Audit(List<string> events) : IAuditService
    {
        public List<AuditEntry> Entries { get; } = [];
        public void Record(AuditEntry entry) { events.Add("audit:" + entry.EventType); Entries.Add(entry); }
        public IReadOnlyList<AuditEntry> Query(AuditQuery query) => Entries;
        public void Clear() => Entries.Clear();
    }

    private sealed class Recents(List<string> events) : IRecentConnectionService
    {
        public int Count { get; private set; }
        public void Record(ServerHost host, ContainerModel? container = null) { Count++; events.Add("recent"); }
        public void ToggleFavorite(ServerHost host, ContainerModel? container = null) { }
        public IReadOnlyList<ConnectionTarget> Favorites() => [];
        public IReadOnlyList<ConnectionTarget> Recent() => [];
        public void Clear() { }
        public void Remove(string hostId, string? containerId = null) { }
    }

    private sealed class Config : IConfigRepository
    {
        private AppConfig value = new() { Groups = [new ServerGroup { Id = "prod", Name = "Production" }] };
        public AppConfig Load() => value;
        public void Save(AppConfig config) => value = config;
    }
}
