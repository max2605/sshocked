using Azure.Core;
using Azure.Identity;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class AzureKeyVaultCredentialFactory
{
    public static TokenCredential Create(SecretProviderConfig config)
    {
        var credentials = new List<TokenCredential> { new AzureCliCredential(new AzureCliCredentialOptions { TenantId = config.TenantId }) };
        var tenant = config.TenantId ?? Environment.GetEnvironmentVariable(AzureKeyVaultProviderConstants.TenantEnvironment);
        var id = config.ClientId ?? Environment.GetEnvironmentVariable(AzureKeyVaultProviderConstants.ClientIdEnvironment);
        var secret = Environment.GetEnvironmentVariable(AzureKeyVaultProviderConstants.ClientSecretEnvironment);
        var certificate = Environment.GetEnvironmentVariable(AzureKeyVaultProviderConstants.ClientCertificateEnvironment);
        if (!string.IsNullOrWhiteSpace(tenant) && !string.IsNullOrWhiteSpace(id))
        {
            if (!string.IsNullOrWhiteSpace(secret)) credentials.Add(new ClientSecretCredential(tenant, id, secret));
            else if (!string.IsNullOrWhiteSpace(certificate)) credentials.Add(new ClientCertificateCredential(tenant, id, certificate));
        }
        credentials.Add(new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = config.TenantId, ManagedIdentityClientId = config.ClientId, ExcludeInteractiveBrowserCredential = false }));
        return new ChainedTokenCredential(credentials.ToArray());
    }
}
