namespace Sshocked.Models;

public sealed record DockerScanResult(ServerHost Host, DockerHostInfo? Info, DockerActionState State, string? Error = null)
{
    public bool Success => State == DockerActionState.Succeeded;
}
