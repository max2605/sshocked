using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISecretProvider
{
    string ProviderName { get; }

    Task<SecretResult> GetSecretAsync(string secretId, SecretType type, CancellationToken ct = default);
    Task<List<SecretMetadata>> ListSecretsAsync(SecretType? filter = null, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
