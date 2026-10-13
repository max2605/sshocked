namespace Sshocked.Tests;

internal sealed class MonitoringTestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public override long GetTimestamp() => _now.UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(TimeSpan duration) => _now += duration;
}
