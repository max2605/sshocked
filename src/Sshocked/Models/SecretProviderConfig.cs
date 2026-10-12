namespace Sshocked.Models;

public sealed class SecretProviderConfig
{
    public string Name { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "AzureKeyVault";
    public bool IsDefault { get; set; }
    public string? VaultUri { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public int CacheTtlSeconds { get; set; } = 60;
}
