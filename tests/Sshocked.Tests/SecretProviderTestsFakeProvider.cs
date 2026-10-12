using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SecretProviderTestsFakeProvider : ISecretProvider
{
    public string Value { get; set; } = "super-secret";
    public int Accesses { get; private set; }
    public string ProviderName => "prod";

    public Task<SecretResult> GetSecret(string secretId, SecretType type, CancellationToken ct = default)
    {
        Accesses++;
        return Task.FromResult(new SecretResult { Success = true, Value = Value });
    }

    public Task<List<SecretMetadata>> ListSecrets(SecretType? filter = null, CancellationToken ct = default) => Task.FromResult(new List<SecretMetadata>());
    public Task<bool> TestConnection(CancellationToken ct = default) => Task.FromResult(true);
}
