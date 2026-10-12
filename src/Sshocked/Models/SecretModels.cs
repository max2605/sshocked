namespace Sshocked.Models;

public enum SecretType { SshPrivateKey, SshPublicKey, Password, ConnectionString, Certificate }

public sealed class SecretRef
{
    public string? ProviderName { get; set; }
    public string SecretId { get; set; } = string.Empty;
    public SecretType SecretType { get; set; }
}

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

// Deliberately not a record: generated ToString must never reveal a secret value.
public sealed class SecretResult
{
    public bool Success { get; init; }
    public string? Value { get; init; }
    public string? TempFilePath { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record SecretMetadata(string Name, string Id, SecretType? SecretType, string? ContentType);
