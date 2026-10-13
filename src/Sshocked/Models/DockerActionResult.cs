namespace Sshocked.Models;

public sealed record DockerActionResult(ConnectionTarget Target, DockerActionState State, int? ExitCode = null,
    string Output = "", string? Error = null)
{
    public bool Success => State == DockerActionState.Succeeded;
}
