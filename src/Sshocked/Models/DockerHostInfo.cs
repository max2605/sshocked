namespace Sshocked.Models;

public sealed class DockerHostInfo
{
    public bool DockerAvailable { get; set; }
    public string? ServerVersion { get; set; }
    public string? OsType { get; set; }
    public string? Architecture { get; set; }
    public int? ContainersRunning { get; set; }
    public int? ContainersTotal { get; set; }
    public DateTime? LastScannedAt { get; set; }
    public string? ScanError { get; set; }
    public string? ScanWarning { get; set; }
    public List<ContainerModel> Containers { get; set; } = [];
    public List<ComposeStackModel> ComposeStacks { get; set; } = [];
}
