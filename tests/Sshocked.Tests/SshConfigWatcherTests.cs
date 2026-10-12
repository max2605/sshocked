using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SshConfigWatcherTests
{
    [Fact]
    public void DebounceUsesTheLatestDeadlineAndIgnoresQueuedCallbacksFromPreviousWatchers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-debounce-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var clock = new SshConfigWatcherTestsManualClock();
            using var watcher = new SshConfigWatcher(NullLogger<SshConfigWatcher>.Instance, clock);
            var count = 0;
            watcher.Changed += (_, _) => count++;
            watcher.StartWatching([Path.Combine(directory, "config")]);
            watcher.ScheduleNotification();
            clock.Advance(TimeSpan.FromMilliseconds(500), fireTimer: false);
            var queuedCallback = clock.CaptureCallback();
            watcher.ScheduleNotification();
            queuedCallback(); // A timer callback was queued before the newer change rescheduled it.
            Assert.Equal(0, count);
            clock.Advance(TimeSpan.FromMilliseconds(499));
            Assert.Equal(0, count);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(1, count);
            queuedCallback(); // An already completed burst must not be announced twice.
            Assert.Equal(1, count);

            watcher.StartWatching([Path.Combine(directory, "config")]);
            watcher.ScheduleNotification();
            queuedCallback(); // A previous watcher generation cannot consume the new pending change.
            Assert.Equal(1, count);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.Equal(2, count);
            var stoppedCallback = clock.CaptureCallback();
            watcher.StopWatching();
            stoppedCallback();
            Assert.Equal(2, count);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task WatcherReportsRealWritesAndAtomicRenames()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config");
            File.WriteAllText(path, "initial");
            using var watcher = new SshConfigWatcher(NullLogger<SshConfigWatcher>.Instance);
            var edits = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var renamed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            watcher.Changed += (_, _) =>
            {
                Interlocked.Increment(ref count);
                try
                {
                    var contents = File.ReadAllText(path);
                    if (contents == "edit 2") edits.TrySetResult();
                    if (contents == "replacement") renamed.TrySetResult();
                }
                catch (IOException) { } // An editor can briefly have the file open or absent.
            };
            watcher.StartWatching([path]);
            for (var i = 0; i < 3; i++)
            {
                File.WriteAllText(path, "edit " + i);
            }
            await edits.Task.WaitAsync(TimeSpan.FromSeconds(8));
            // Native filesystem event delivery can produce several callbacks under load.
            // Test their coalescing deterministically above and stabilize this integration phase.
            var beforeRename = await WaitForQuiet(() => Volatile.Read(ref count));
            var replacement = Path.Combine(directory, "temporary");
            File.WriteAllText(replacement, "replacement");
            File.Move(replacement, path, overwrite: true);
            await renamed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.True(Volatile.Read(ref count) > beforeRename);
            watcher.StopWatching();
            Assert.False(watcher.IsWatching);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<int> WaitForQuiet(Func<int> count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = count();
            await Task.Delay(750);
            if (snapshot == count()) return snapshot;
        }
        throw new TimeoutException("SSH config watcher did not settle before the atomic rename test.");
    }
}
