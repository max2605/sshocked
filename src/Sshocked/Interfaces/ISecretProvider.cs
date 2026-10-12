using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISecretProvider
{
    string ProviderName { get; }

    Task<SecretResult> GetSecret(string secretId, SecretType type, CancellationToken ct = default);
    Task<List<SecretMetadata>> ListSecrets(SecretType? filter = null, CancellationToken ct = default);
    Task<bool> TestConnection(CancellationToken ct = default);
}
