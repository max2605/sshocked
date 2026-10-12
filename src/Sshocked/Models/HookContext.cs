namespace Sshocked.Models;

public sealed record HookContext
{
    public DateTimeOffset ConnectionTimestamp { get; init; } = DateTimeOffset.UtcNow;
    public string ConfigDirectory { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
    public double? DurationSeconds { get; init; }
}
