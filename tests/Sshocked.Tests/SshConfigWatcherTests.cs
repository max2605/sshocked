using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SshConfigWatcherTests
{
    [Fact]
    public async Task WatcherDebouncesWritesAndReportsAtomicRenames()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config");
            File.WriteAllText(path, "initial");
            using var watcher = new SshConfigWatcher(NullLogger<SshConfigWatcher>.Instance);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            watcher.Changed += (_, _) => { Interlocked.Increment(ref count); completion.TrySetResult(); };
            watcher.StartWatching([path]);
            for (var i = 0; i < 3; i++)
            {
                File.WriteAllText(path, "edit " + i);
                await Task.Delay(50);
            }
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await Task.Delay(650);
            Assert.Equal(1, Volatile.Read(ref count));
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var replacement = Path.Combine(directory, "temporary");
            File.WriteAllText(replacement, "replacement");
            File.Move(replacement, path, overwrite: true);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
            watcher.StopWatching();
            Assert.False(watcher.IsWatching);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
