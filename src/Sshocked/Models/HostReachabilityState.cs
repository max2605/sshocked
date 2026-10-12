namespace Sshocked.Models;

public enum HostReachabilityState
{
    Unchecked,
    Checking,
    Online,
    Offline,
    Stale,
    ViaBastion,
    ViaJumpHost
}
