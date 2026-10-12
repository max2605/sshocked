namespace Sshocked.Tests;

internal sealed class SshConfigWatcherTestsManualTimer(SshConfigWatcherTestsManualClock clock, TimerCallback callback, object? state) : ITimer
{
    private long _due = long.MaxValue;
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.GetTimestamp() + dueTime.Ticks;
        return true;
    }

    public void FireIfDue()
    {
        if (_due > clock.GetTimestamp())
            return;
        _due = long.MaxValue;
        callback(state);
    }

    public void InvokeQueuedCallback() => callback(state);
    public void Dispose() => _due = long.MaxValue;
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
