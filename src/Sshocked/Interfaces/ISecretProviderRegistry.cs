using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISecretProviderRegistry
{
    IReadOnlyList<SecretProviderConfig> GetConfigurations();
    ISecretProvider GetProvider(string? name = null);
    Task<SecretResult> Resolve(SecretRef reference, CancellationToken ct = default);
    void ClearCache();
}
