namespace Sshocked.Models;

public sealed class ForwardingStatus
{
    public string ProfileId { get; set; } = "";
    public string Name { get; set; } = "";
    public string HostId { get; set; } = "";
    public string Route { get; set; } = "";
    public ForwardingState State { get; set; }
    public int OwnerProcessId { get; set; }
    public DateTimeOffset OwnerStartedUtc { get; set; }
    public int? SshProcessId { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public int? ExitCode { get; set; }
    public string? Error { get; set; }
}
