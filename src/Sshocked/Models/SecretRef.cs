namespace Sshocked.Models;

public sealed class SecretRef
{
    public string? ProviderName { get; set; }
    public string SecretId { get; set; } = string.Empty;
    public SecretType SecretType { get; set; }
}
