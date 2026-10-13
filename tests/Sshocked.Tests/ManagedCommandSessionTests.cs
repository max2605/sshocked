using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ManagedCommandSessionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-command-session-" + Guid.NewGuid().ToString("N"));
    private readonly MacroTestDependencies dependencies = new();
    private readonly LocalShellTestRunner runner = new();
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
    private readonly ServerHost host = new() { Id = "one", Alias = "web", HostName = "localhost", User = "user" };

    private RemoteCommandSessionService Service(bool record = false) => new(dependencies, runner, dependencies,
        new JsonConfigRepository(Path.Combine(directory, "appconfig.json")), new ManagedSessionRecordingPolicy(record), new SessionRepository(Path.Combine(directory, "sessions")));

    [Fact]
    public async Task ShellStateAndBothOutputStreamsPersistWithoutProtocolMarkers()
    {
        var output = new List<string>();
        await using (var session = await Service(true).Open(host, timeout.Token))
        {
            Assert.Equal(0, (await session.Execute("cd /; export SSHOCKED_TEST_VALUE=retained", output.Add)).ExitCode);
            var result = await session.Execute("printf '%s:%s' \"$PWD\" \"$SSHOCKED_TEST_VALUE\"; printf 'stderr-before-failure' >&2; false", output.Add);
            Assert.Equal("/:retained", result.StandardOutput);
            Assert.Equal("stderr-before-failure", result.StandardError);
            Assert.Equal(1, result.ExitCode);
            var next = await session.Execute("printf 'next'", output.Add);
            Assert.Equal("next", next.StandardOutput); Assert.Empty(next.StandardError);
        }
        Assert.DoesNotContain("SSHOCKED_", string.Concat(output));
        Assert.True(dependencies.LastLease!.IsDisposed);
        var recordings = new SessionRepository(Path.Combine(directory, "sessions"));
        var recording = Assert.Single(recordings.List());
        Assert.Equal(0, recording.ExitCode);
        Assert.Contains("stderr-before-failure", string.Concat(recordings.ReadEvents(recording).Select(entry => entry.Data)));
        Assert.DoesNotContain("SSHOCKED_", string.Concat(recordings.ReadEvents(recording).Select(entry => entry.Data)));
    }

    [Fact]
    public async Task CommandStdinCannotConsumeFutureCommandsOrAuthentication()
    {
        await using var session = await Service().Open(host, timeout.Token);
        var read = await session.Execute("read value", _ => { });
        Assert.NotEqual(0, read.ExitCode);
        Assert.Equal("after-read", (await session.Execute("printf 'after-read'", _ => { })).StandardOutput);
    }

    [Fact]
    public async Task PrintfFunctionAndOutputRedirectionCannotHideCommandCompletion()
    {
        await using var session = await Service().Open(host, timeout.Token);
        Assert.Equal(0, (await session.Execute("printf() { command printf 'custom'; }; printf", _ => { })).ExitCode);
        Assert.Equal("custom", (await session.Execute("printf", _ => { })).StandardOutput);
        Assert.Equal(0, (await session.Execute("exec >/dev/null 2>/dev/null", _ => { })).ExitCode);
        Assert.Equal(0, (await session.Execute("export TEST_PERSISTED=yes", _ => { })).ExitCode);
    }

    [Fact]
    public async Task ResolvedCredentialLiteralsAreRejectedForRecordingAndRedactedFromAudit()
    {
        dependencies.PreparedPassword = "saved-password-value";
        await using var session = await Service().Open(host, timeout.Token);
        Assert.Null(host.SshPassword);
        Assert.Throws<ArgumentException>(() => session.ValidateRecordingCommand("printf saved-password-value"));
        await session.Execute("printf saved-password-value", _ => { });
        Assert.DoesNotContain(dependencies.PreparedPassword, Assert.Single(dependencies.Events, entry => entry.EventType == "command.session").Action);
    }

    [Fact]
    public async Task LargeUnicodeOutputKeepsDecoderStateAndRecordingValid()
    {
        await using var session = await Service(true).Open(host, timeout.Token);
        var result = await session.Execute("i=0; while [ \"$i\" -lt 1025 ]; do command printf 'abc😀'; i=$((i+1)); done", _ => { });
        Assert.Equal(string.Concat(Enumerable.Repeat("abc😀", 1025)), result.StandardOutput);
    }

    [Fact]
    public async Task CancellationKillsShellAndReleasesPreparedCredentials()
    {
        await using var session = await Service().Open(host, timeout.Token);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Execute("sleep 30", _ => { }, cancellation.Token));
        Assert.True(dependencies.LastLease!.IsDisposed);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), "Cancellation must terminate the process rather than wait for sleep to finish.");
    }

    [Fact]
    public async Task ShellExitIsReportedWithoutWaitingForMissingMarkers()
    {
        await using var session = await Service().Open(host, timeout.Token);
        await Assert.ThrowsAsync<IOException>(() => session.Execute("exit 7", _ => { }, timeout.Token));
        Assert.True(dependencies.LastLease!.IsDisposed);
    }

    [Fact]
    public async Task CommandLoopRecordsOnlyCommandsBetweenStartAndStop()
    {
        var config = new JsonConfigRepository(Path.Combine(directory, "appconfig.json"));
        var macros = new MacroService(config, dependencies, dependencies);
        foreach (var command in new[] { "printf ignored", ":record", "cd /", "printf recorded", ":stop", "printf ignored-again", ":save test", ":exit" }) dependencies.Input.Enqueue(command);
        await new ManagedCommandSessionService(Service(), macros, dependencies).Show(host, timeout.Token);
        Assert.Equal(new[] { "cd /", "printf recorded" }, Assert.Single(macros.List()).Commands);
        Assert.DoesNotContain(Assert.Single(macros.List()).Commands, command => command.StartsWith(':'));
    }

    public void Dispose() { timeout.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
