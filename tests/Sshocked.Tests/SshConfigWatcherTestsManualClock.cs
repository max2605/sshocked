namespace Sshocked.Tests;

internal sealed class SshConfigWatcherTestsManualClock : TimeProvider
{
    private long _ticks;
    private SshConfigWatcherTestsManualTimer? _timer;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _timer = new SshConfigWatcherTestsManualTimer(this, callback, state);
        _timer.Change(dueTime, period);
        return _timer;
    }

    public void Advance(TimeSpan elapsed, bool fireTimer = true)
    {
        _ticks += elapsed.Ticks;
        if (fireTimer)
            _timer?.FireIfDue();
    }

    public Action CaptureCallback() => _timer!.InvokeQueuedCallback;
}
