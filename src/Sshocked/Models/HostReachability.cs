namespace Sshocked.Models;

public sealed record HostReachability(HostReachabilityState State, DateTimeOffset? LastCheckedUtc = null, bool? IsReachable = null);
