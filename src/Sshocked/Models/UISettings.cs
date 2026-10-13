namespace Sshocked.Models;

public sealed class UISettings
{
    public bool ShowReachabilityStatus { get; set; } = true;
    public bool EnableReachabilityChecks { get; set; } = true;
    public bool ShowLastConnectedColumn { get; set; } = true;
    public bool CompactMode { get; set; }
    public string Theme { get; set; } = "Dark";
    public bool ShowBreadcrumbs { get; set; } = true;
    public bool ShowFooter { get; set; } = true;
}
