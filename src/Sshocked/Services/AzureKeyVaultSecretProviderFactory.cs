using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class AzureKeyVaultSecretProviderFactory : ISecretProviderFactory
{
    public string ProviderType => AzureKeyVaultProviderConstants.ProviderType;
    public ISecretProvider Create(SecretProviderConfig config) => new AzureKeyVaultSecretProvider(config);
}
