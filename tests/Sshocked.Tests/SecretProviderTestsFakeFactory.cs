using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SecretProviderTestsFakeFactory : ISecretProviderFactory
{
    public SecretProviderTestsFakeProvider Provider { get; } = new();
    public string ProviderType => "Fake";

    public ISecretProvider Create(SecretProviderConfig config) => Provider;
}
