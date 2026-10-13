namespace Sshocked.Models;

public sealed record HostResolution(
    HostResolutionState State,
    IReadOnlyList<string>? Addresses = null,
    string? Error = null,
    DateTimeOffset? LastCheckedUtc = null,
    bool IsRefreshing = false);
