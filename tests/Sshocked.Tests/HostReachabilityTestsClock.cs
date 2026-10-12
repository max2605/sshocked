namespace Sshocked.Tests;

internal sealed class HostReachabilityTestsClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;
}
