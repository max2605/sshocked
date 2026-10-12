namespace Sshocked.Models;

public sealed class AzureBastionConfig
{
    public string BastionName { get; set; } = string.Empty;
    public string BastionResourceGroup { get; set; } = string.Empty;
    public string? TargetResourceId { get; set; }
    public string? TargetIpAddress { get; set; }
    public int TargetPort { get; set; } = 22;
    /// <summary>Zero selects an available loopback port for each connection.</summary>
    public int LocalPort { get; set; }
    public BastionAuthType AuthType { get; set; } = BastionAuthType.SshKey;
    public string? Username { get; set; }
    public string? SshKeyPath { get; set; }
    public string? SubscriptionId { get; set; }
    public string? TenantId { get; set; }
    public BastionConnectionMode ConnectionMode { get; set; } = BastionConnectionMode.Native;
}
