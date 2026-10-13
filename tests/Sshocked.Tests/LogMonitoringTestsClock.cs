namespace Sshocked.Tests;

internal sealed class LogMonitoringTestsClock : TimeProvider
{
    private long milliseconds;
    private DateTimeOffset utc = DateTimeOffset.Parse("2026-10-13T10:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture);
    public override long TimestampFrequency => 1000;
    public override DateTimeOffset GetUtcNow() => utc;
    public override long GetTimestamp() => milliseconds;
    public void Advance(TimeSpan elapsed) { milliseconds += (long)elapsed.TotalMilliseconds; utc += elapsed; }
    public void JumpUtc(TimeSpan difference) => utc += difference;
}
