using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISecretProvider
{
    string ProviderName { get; }
    Task<SecretResult> GetSecretAsync(string secretId, SecretType type, CancellationToken ct = default);
    Task<List<SecretMetadata>> ListSecretsAsync(SecretType? filter = null, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

public interface ISecretProviderFactory
{
    string ProviderType { get; }
    ISecretProvider Create(SecretProviderConfig config);
}

public interface ISecretProviderRegistry
{
    IReadOnlyList<SecretProviderConfig> GetConfigurations();
    ISecretProvider GetProvider(string? name = null);
    Task<SecretResult> ResolveAsync(SecretRef reference, CancellationToken ct = default);
    void ClearCache();
}
