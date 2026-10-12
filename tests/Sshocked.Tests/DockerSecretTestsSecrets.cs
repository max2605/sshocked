using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class DockerSecretTestsSecrets : ISecretProviderRegistry
{
    public int ResolveCount { get; private set; }

    public IReadOnlyList<SecretProviderConfig> GetConfigurations() => [];
    public ISecretProvider GetProvider(string? name = null) => throw new NotSupportedException();
    public Task<SecretResult> Resolve(SecretRef reference, CancellationToken ct = default)
    {
        ResolveCount++;
        return Task.FromResult(new SecretResult { Success = true, Value = DockerSecretTests.Password });
    }

    public void ClearCache()
    {
    }
}
