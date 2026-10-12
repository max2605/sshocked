using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SessionRecordingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-sessions-" + Guid.NewGuid().ToString("N"));
    private SessionRepository Repository() => new(directory);
    private static ServerHost Host(string alias = "web") => new() { Id = alias, Alias = alias, HostName = "example", User = "alice" };

    [Fact]
    public void RecordingDefaultsOffAndExplicitHostOverridesAndExclusionsApply()
    {
        var config = new SessionRecordingTestsConfig();
        var service = new SessionRecordingService(config, new ExternalProcessRunner(), Repository(), NullLogger<SessionRecordingService>.Instance);
        var host = Host();
        Assert.False(service.ShouldRecord(host));
        host.RecordSession = true;
        Assert.True(service.ShouldRecord(host));
        config.Value.SessionRecording.ExcludeHosts["WEB"] = true;
        Assert.False(service.ShouldRecord(host));
        config.Value.SessionRecording.ExcludeHosts.Clear();
        host.RecordSession = null;
        config.Value.SessionRecording.IncludeHosts["web"] = true;
        Assert.True(service.ShouldRecord(host));
        config.Value.SessionRecording.IncludeHosts.Clear();
        config.Value.SessionRecording.Enabled = true;
        Assert.True(service.ShouldRecord(host));
        host.RecordSession = false;
        Assert.False(service.ShouldRecord(host));
    }

    [Fact]
    public void CastHeaderEventsAndMetadataRoundTripWithEscapedOutput()
    {
        var repository = Repository();
        using var writer = repository.Create(Host(), width: 100, height: 30);
        writer.Append("hello\n\"quotes\" \u001b[31m red");
        writer.Append(" and stderr\n");
        writer.Complete(7);
        var metadata = Assert.Single(repository.List());
        Assert.Equal(7, metadata.ExitCode);
        Assert.Equal("output-only", metadata.RecordingType);
        Assert.True(metadata.FileSize > 0);
        Assert.NotNull(metadata.EndedAt);
        var lines = File.ReadAllLines(repository.ResolvePath(metadata.RelativeFilePath));
        using var header = JsonDocument.Parse(lines[0]);
        Assert.Equal(2, header.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(100, header.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(30, header.RootElement.GetProperty("height").GetInt32());
        var events = repository.ReadEvents(metadata).ToList();
        Assert.Equal(2, events.Count);
        Assert.True(events[1].Time >= events[0].Time);
        Assert.Equal("hello\n\"quotes\" \u001b[31m red", events[0].Data);
        Assert.Equal(metadata.Id, repository.Find(metadata.Id[..8]).Id);
    }

    [Fact]
    public void UntimedRecordingStillUsesValidCastEventsAtZero()
    {
        var repository = Repository();
        using var writer = repository.Create(Host(), captureTiming: false);
        writer.Append("one"); writer.Append("two"); writer.Complete(0);
        Assert.All(repository.ReadEvents(writer.Metadata), entry => Assert.Equal(0, entry.Time));
    }

    [Fact]
    public void CompressedRecordingCanBeReplayedExportedAndDeleted()
    {
        var repository = Repository();
        using var writer = repository.Create(Host());
        writer.Append("hello\nworld\n"); writer.Complete(0, compress: true);
        var metadata = Assert.Single(repository.List());
        Assert.True(metadata.Compressed);
        Assert.EndsWith(".cast.gz", metadata.RelativeFilePath);
        Assert.Equal("hello\nworld\n", Assert.Single(repository.ReadEvents(metadata)).Data);
        var castPath = Path.Combine(directory, "export.cast");
        repository.Export(metadata.Id, castPath);
        using var header = JsonDocument.Parse(File.ReadLines(castPath).First());
        Assert.Equal(2, header.RootElement.GetProperty("version").GetInt32());
        var textPath = Path.Combine(directory, "export.txt");
        repository.Export(metadata.Id, textPath, "text");
        Assert.Equal("hello\nworld\n", File.ReadAllText(textPath));
        repository.Delete(metadata.Id);
        Assert.Empty(repository.List());
        Assert.False(File.Exists(repository.ResolvePath(metadata.RelativeFilePath)));
    }

    [Fact]
    public void RetentionDeletesOldRecordingsAndKeepsActiveSession()
    {
        var repository = Repository();
        using (var old = repository.Create(Host("old")))
        {
            old.Metadata.StartedAt = DateTimeOffset.UtcNow.AddDays(-40);
            old.Append("expired"); old.Complete(0);
        }
        using var active = repository.Create(Host("active"));
        active.Metadata.StartedAt = DateTimeOffset.UtcNow.AddDays(-40);
        // The creation metadata stays on disk until completion; direct active deletion is also guarded.
        Assert.Throws<InvalidOperationException>(() => repository.Delete(active.Metadata.Id));
        repository.ApplyRetention(new() { RetentionDays = 30 });
        Assert.Equal("active", Assert.Single(repository.List()).HostAlias);
        active.Complete(0);
    }

    [Fact]
    public void SizeLimitRemovesOldestCompletedRecording()
    {
        var repository = Repository();
        using (var old = repository.Create(Host("old")))
        {
            old.Metadata.StartedAt = DateTimeOffset.UtcNow.AddDays(-1);
            old.Append(new string('x', 2 * 1024 * 1024)); old.Complete(0);
        }
        using (var recent = repository.Create(Host("recent"))) { recent.Append("keep"); recent.Complete(0); }
        repository.ApplyRetention(new() { RetentionDays = 0, MaxTotalSizeMb = 1 });
        Assert.Equal("recent", Assert.Single(repository.List()).HostAlias);
    }

    [Fact]
    public void RetentionPreservesIncompleteRecordingsOwnedByAnotherRepository()
    {
        var owner = Repository();
        using var active = owner.Create(Host("external-active"));
        active.Append(new string('x', 2 * 1024 * 1024));
        var observer = Repository();
        observer.ApplyRetention(new() { RetentionDays = 0, MaxTotalSizeMb = 1 });
        Assert.Single(observer.List());
        Assert.True(File.Exists(observer.ResolvePath(active.Metadata.RelativeFilePath)));
        active.Append("still available");
        active.Complete(0);
        observer.ApplyRetention(new() { RetentionDays = 0, MaxTotalSizeMb = 1 });
        Assert.Empty(observer.List());
    }

    [Fact]
    public async Task ConcurrentRepositoriesCanListAndApplySizeRetentionWithoutDeletingTheSameSnapshot()
    {
        var owner = Repository();
        var first = Repository();
        var second = Repository();
        for (var round = 0; round < 3; round++)
        {
            for (var index = 0; index < 64; index++)
            {
                using var writer = owner.Create(Host($"round-{round}-host-{index}"));
                writer.Append(new string('x', 32 * 1024));
                writer.Complete(0);
            }

            using var start = new ManualResetEventSlim();
            var settings = new SessionRecordingSettings { RetentionDays = 0, MaxTotalSizeMb = 1 };
            var jobs = new[]
            {
                Task.Run(() => { start.Wait(); first.ApplyRetention(settings); }),
                Task.Run(() => { start.Wait(); second.ApplyRetention(settings); }),
                Task.Run(() => { start.Wait(); for (var index = 0; index < 30; index++) owner.List(int.MaxValue); })
            };
            start.Set();
            await Task.WhenAll(jobs);
            var remaining = owner.List(int.MaxValue);
            Assert.NotEmpty(remaining);
            Assert.True(remaining.Sum(session => session.FileSize) <= 1024 * 1024);
            Assert.All(remaining, session => Assert.NotNull(session.EndedAt));
        }
    }

    [Theory]
    [InlineData("../outside.cast")]
    [InlineData("../../outside.cast")]
    public void RepositoryRejectsTraversalPaths(string path) => Assert.Throws<ArgumentException>(() => Repository().ResolvePath(path));

    [Fact]
    public void RepositorySkipsMalformedMetadataInsteadOfFailingWholeList()
    {
        var repository = Repository();
        File.WriteAllText(Path.Combine(directory, "broken.json"), "{invalid}");
        using var writer = repository.Create(Host()); writer.Append("valid"); writer.Complete(0);
        Assert.Single(repository.List());
    }

    [Fact]
    public async Task RecorderCapturesBothStreamsAndReturnsNativeExitCode()
    {
        var config = new SessionRecordingTestsConfig();
        config.Value.SessionRecording.Compress = false;
        var repository = Repository();
        var service = new SessionRecordingService(config, new ExternalProcessRunner(), repository, NullLogger<SessionRecordingService>.Instance);
        var info = Child("echo stdout-message & echo stderr-message 1>&2 & exit /b 7", "printf 'stdout-message\\n'; printf 'stderr-message\\n' >&2; exit 7");
        Assert.Equal(7, await service.Record(info, Host()));
        Assert.False(info.RedirectStandardInput);
        var metadata = Assert.Single(repository.List());
        Assert.Equal(7, metadata.ExitCode);
        var output = string.Concat(repository.ReadEvents(metadata).Select(entry => entry.Data));
        Assert.Contains("stdout-message", output);
        Assert.Contains("stderr-message", output);
    }

    [Fact]
    public async Task RecorderCancellationKillsChildAndFinalizesPartialOutput()
    {
        var config = new SessionRecordingTestsConfig();
        config.Value.SessionRecording.Compress = false;
        var repository = Repository();
        var runner = new SessionRecordingTestsObservedRunner();
        var service = new SessionRecordingService(config, runner, repository, NullLogger<SessionRecordingService>.Instance);
        using var cancellation = new CancellationTokenSource();
        var info = Child("echo before-cancel & ping -n 30 127.0.0.1 > nul", "printf before-cancel; sleep 30");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.Record(info, Host(), cancellation.Token, onStarted: () =>
        {
            using var child = Process.GetProcessById(runner.ProcessId);
            Assert.False(child.HasExited);
            cancellation.Cancel();
            return Task.CompletedTask;
        }));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(runner.ProcessId));
        var metadata = Assert.Single(repository.List());
        Assert.NotNull(metadata.EndedAt);
        Assert.Null(metadata.ExitCode);
    }

    [Fact]
    public async Task RecordingCallsStartedCallbackWhileChildIsAlive()
    {
        var repository = Repository();
        var runner = new SessionRecordingTestsObservedRunner();
        var service = new SessionRecordingService(new SessionRecordingTestsConfig(), runner, repository, NullLogger<SessionRecordingService>.Instance);
        var started = false;
        var info = Child("ping -n 2 127.0.0.1 > nul", "sleep 1");
        await service.Record(info, Host(), onStarted: () =>
        {
            started = true;
            using var process = Process.GetProcessById(runner.ProcessId);
            Assert.False(process.HasExited);
            return Task.CompletedTask;
        });
        Assert.True(started);
        Assert.NotNull(Assert.Single(repository.List()).EndedAt);
    }

    [Fact]
    public async Task StartedCallbackFailureKillsChildAndFinalizesRecording()
    {
        var repository = Repository();
        var runner = new SessionRecordingTestsObservedRunner();
        var service = new SessionRecordingService(new SessionRecordingTestsConfig(), runner, repository, NullLogger<SessionRecordingService>.Instance);
        var info = Child("ping -n 30 127.0.0.1 > nul", "sleep 30");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Record(info, Host(), onStarted:
            () => throw new InvalidOperationException("setup hook failed")));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(runner.ProcessId));
        var metadata = Assert.Single(repository.List());
        Assert.NotNull(metadata.EndedAt);
        Assert.Null(metadata.ExitCode);
    }

    [Fact]
    public async Task OneFailedReaderKillsChildWithoutWaitingForOtherOpenStream()
    {
        var repository = Repository();
        var runner = new SessionRecordingTestsObservedRunner { CloseStandardOutput = true };
        var service = new SessionRecordingService(new SessionRecordingTestsConfig(), runner, repository, NullLogger<SessionRecordingService>.Instance);
        var info = Child("ping -n 30 127.0.0.1 > nul", "sleep 30");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.Record(info, Host(), deadline.Token));
        Assert.False(deadline.IsCancellationRequested);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(runner.ProcessId));
        Assert.NotNull(Assert.Single(repository.List()).EndedAt);
    }

    [Fact]
    public async Task FinalizationFailurePreservesPrimaryFailureAndExistingGzipFile()
    {
        var repository = Repository();
        var service = new SessionRecordingService(new SessionRecordingTestsConfig(), new ExternalProcessRunner(), repository, NullLogger<SessionRecordingService>.Instance);
        string? existingGzip = null;
        var info = Child("ping -n 30 127.0.0.1 > nul", "sleep 30");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Record(info, Host(), onStarted: () =>
        {
            var active = Assert.Single(repository.List());
            existingGzip = repository.ResolvePath(active.RelativeFilePath) + ".gz";
            File.WriteAllText(existingGzip, "existing file belongs to caller");
            throw new InvalidOperationException("primary setup failure");
        }));
        Assert.Equal("primary setup failure", exception.Message);
        Assert.Equal("existing file belongs to caller", File.ReadAllText(existingGzip!));
    }

    [Fact]
    public async Task RecorderPreservesUnicodeAcrossReadBufferBoundaries()
    {
        var repository = Repository();
        var service = new SessionRecordingService(new SessionRecordingTestsConfig(), new ExternalProcessRunner(), repository, NullLogger<SessionRecordingService>.Instance);
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
            info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); [Console]::Write(('x'*4095)+[char]::ConvertFromUtf32(0x1F600)+'z')");
        }
        else info = Child("", "printf '%4095s' ''; printf '\\360\\237\\230\\200z'");
        Assert.Equal(0, await service.Record(info, Host()));
        var output = string.Concat(repository.ReadEvents(Assert.Single(repository.List())).Select(entry => entry.Data));
        Assert.Contains("\U0001F600z", output);
        Assert.DoesNotContain('\uFFFD', output);
    }

    [Theory]
    [InlineData(RecordingMode.FullTerminal)]
    [InlineData(RecordingMode.ScriptFormat)]
    public async Task RecorderRejectsUnavailablePtyModesExplicitly(RecordingMode mode)
    {
        var config = new SessionRecordingTestsConfig(); config.Value.SessionRecording.Mode = mode;
        var service = new SessionRecordingService(config, new ExternalProcessRunner(), Repository(), NullLogger<SessionRecordingService>.Instance);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.Record(Child("echo test", "echo test"), Host()));
    }

    [Fact]
    public async Task RecorderRejectsInputCaptureExplicitly()
    {
        var config = new SessionRecordingTestsConfig(); config.Value.SessionRecording.CaptureInput = true;
        var service = new SessionRecordingService(config, new ExternalProcessRunner(), Repository(), NullLogger<SessionRecordingService>.Instance);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.Record(Child("echo test", "echo test"), Host()));
    }

    [Fact]
    public async Task ReplayProducesOutputWithSpeedValidationAndCancellation()
    {
        using var output = new StringWriter();
        await SessionReplayService.Replay(new[] { new SessionEvent(0, "hello"), new SessionEvent(0.01, " world") }, 16, output);
        Assert.Equal("hello world", output.ToString());
        await Assert.ThrowsAsync<ArgumentException>(() => SessionReplayService.Replay([], 0));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SessionReplayService.Replay([new(30, "never")], cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task SessionCommandRejectsMixedDeleteModesAndInvalidSpeed()
    {
        var repository = Repository();
        using var writer = repository.Create(Host()); writer.Append("test"); writer.Complete(0);
        var handler = new SessionCommandHandler(repository, new SessionRecordingTestsConfig());
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Execute("sessions", ["delete", writer.Metadata.Id, "--older", "30d"]));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Execute("sessions", ["replay", writer.Metadata.Id, "--speed", "NaN"]));
        Assert.Single(repository.List());
    }

    private static ProcessStartInfo Child(string windowsCommand, string unixCommand)
    {
        var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh") { UseShellExecute = false };
        info.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        info.ArgumentList.Add(OperatingSystem.IsWindows() ? windowsCommand : unixCommand);
        return info;
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
