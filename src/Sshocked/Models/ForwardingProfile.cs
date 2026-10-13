namespace Sshocked.Models;

public sealed class ForwardingProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string HostId { get; set; } = "";
    public ForwardingKind Kind { get; set; }
    public string BindAddress { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; }
    public string TargetHost { get; set; } = "127.0.0.1";
    public int TargetPort { get; set; }
    public bool AllowNonLoopbackBind { get; set; }
    public int StartupTimeoutSeconds { get; set; } = 30;
}
