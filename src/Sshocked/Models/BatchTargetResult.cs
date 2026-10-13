namespace Sshocked.Models;

public sealed record BatchTargetResult(string HostId, string Alias, BatchTargetStatus Status,
    double DurationSeconds = 0, int? ExitCode = null, string? Error = null);
