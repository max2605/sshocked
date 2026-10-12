using Sshocked.Models;


namespace Sshocked.Tests;

public sealed class SshRunnerLifecycleTests
{
    [Fact]
    public async Task SuccessfulSessionRunsPhasesInOrderAndRecordsOneOutcome()
    {
        var fixture = new SshRunnerLifecycleTestsFixture();
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
        var fixture = new SshRunnerLifecycleTestsFixture();
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
        var fixture = new SshRunnerLifecycleTestsFixture();
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
        var fixture = new SshRunnerLifecycleTestsFixture();
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
        var fixture = new SshRunnerLifecycleTestsFixture();
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
        var fixture = new SshRunnerLifecycleTestsFixture();
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
        var fixture = new SshRunnerLifecycleTestsFixture();
        fixture.Hooks.Results[HookPhase.PostConnect] = false;
        fixture.Hooks.Failures[HookPhase.PreDisconnect] = new IOException("pre cleanup");
        fixture.Hooks.Failures[HookPhase.PostDisconnect] = new IOException("post cleanup");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runner.Connect(fixture.Host));
        Assert.Contains("post-connect", error.Message);
        Assert.Contains("PreDisconnect", fixture.Events);
        Assert.Contains("PostDisconnect", fixture.Events);
        Assert.Single(fixture.Audit.Entries, entry => entry.EventType == "connection.end");
    }
}
