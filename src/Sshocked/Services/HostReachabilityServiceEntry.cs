namespace Sshocked.Services;

internal sealed class HostReachabilityServiceEntry
{
    public readonly object Gate = new();
    public DateTimeOffset? LastChecked;
    public bool? Reachable;
    public bool Checking;
}
