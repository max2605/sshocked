namespace Sshocked.Models;

public class ConnectionHooks
{
    public HookScript? PreConnect { get; set; }
    public HookScript? PostConnect { get; set; }
    public HookScript? PreDisconnect { get; set; }
    public HookScript? PostDisconnect { get; set; }
}
