using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class CrossProcessFileLockTests
{
    [Fact]
    public void EquivalentPathsBlockAnotherThreadAndReleaseAfterDisposal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-file-lock-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.json");
        var equivalent = Path.Combine(directory, ".", "config.json");
        if (OperatingSystem.IsWindows()) equivalent = equivalent.ToUpperInvariant();

        using (CrossProcessFileLock.Acquire(path))
        {
            OnOtherThread(() =>
            {
                var error = Assert.Throws<TimeoutException>(() =>
                {
                    using var other = CrossProcessFileLock.Acquire(equivalent, TimeSpan.FromMilliseconds(100));
                });
                Assert.Contains("another sshocked window", error.Message);
            });

            // Locks for independent files must not unnecessarily serialize unrelated operations.
            OnOtherThread(() =>
            {
                using var otherFile = CrossProcessFileLock.Acquire(Path.Combine(directory, "audit.jsonl"), TimeSpan.Zero);
            });
        }

        OnOtherThread(() =>
        {
            using var released = CrossProcessFileLock.Acquire(equivalent, TimeSpan.Zero);
        });
    }

    [Fact]
    public void AbandonedOwnerCanBeRecoveredAndReleased()
    {
        var path = Path.Combine(Path.GetTempPath(), "sshocked-abandoned-lock-" + Guid.NewGuid().ToString("N"));
        CrossProcessFileLock? abandoned = null;
        var worker = new Thread(() => abandoned = CrossProcessFileLock.Acquire(path));
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));

        using (CrossProcessFileLock.Acquire(path, TimeSpan.FromSeconds(1)))
        {
            Assert.NotNull(abandoned);
        }
        using var reacquired = CrossProcessFileLock.Acquire(path, TimeSpan.Zero);
        GC.KeepAlive(abandoned);
    }

    private static void OnOtherThread(Action action)
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "Synchronous lock test must finish within five seconds.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
