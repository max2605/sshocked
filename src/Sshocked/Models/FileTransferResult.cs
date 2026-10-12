namespace Sshocked.Models;

public sealed record FileTransferResult(ServerHost Host, string LocalPath, int? ExitCode, string? Error)
{
    public bool Success => ExitCode == 0 && Error is null;
}
