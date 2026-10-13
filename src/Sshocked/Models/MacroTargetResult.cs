namespace Sshocked.Models;

public sealed record MacroTargetResult(string HostId, string Alias, bool Success, int? ExitCode, string? Error);
