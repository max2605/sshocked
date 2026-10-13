namespace Sshocked.Models;

public sealed class ForwardingRuntimeRecord
{
    public ForwardingStatus Status { get; set; } = new();
    public string ControlToken { get; set; } = Guid.NewGuid().ToString("N");
}
