namespace Sshocked.Models;

public sealed class GlobalHooks : ConnectionHooks
{
    public List<string> IncludeTags { get; set; } = [];
    public List<string> ExcludeTags { get; set; } = [];
}
