using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class AzureKeyVaultSecretProviderFactory : ISecretProviderFactory
{
    public string ProviderType => "AzureKeyVault";
    public ISecretProvider Create(SecretProviderConfig config) => new AzureKeyVaultSecretProvider(config);
}

public sealed class AzureKeyVaultSecretProvider : ISecretProvider
{
    private readonly SecretClient client;
    private readonly Uri vaultUri;
    public string ProviderName { get; }

    public AzureKeyVaultSecretProvider(SecretProviderConfig config) : this(config, CreateCredential(config)) { }

    public AzureKeyVaultSecretProvider(SecretProviderConfig config, TokenCredential credential)
    {
        ProviderName = config.Name;
        if (!Uri.TryCreate(config.VaultUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Azure Key Vault URI must be an HTTPS vault root, such as https://my-vault.vault.azure.net/.");
        vaultUri = uri;
        client = new SecretClient(vaultUri, credential);
    }

    private static TokenCredential CreateCredential(SecretProviderConfig config)
    {
        var credentials = new List<TokenCredential> { new AzureCliCredential(new AzureCliCredentialOptions { TenantId = config.TenantId }) };
        var tenant = config.TenantId ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
        var id = config.ClientId ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
        var certificate = Environment.GetEnvironmentVariable("AZURE_CLIENT_CERTIFICATE_PATH");
        if (!string.IsNullOrWhiteSpace(tenant) && !string.IsNullOrWhiteSpace(id))
        {
            if (!string.IsNullOrWhiteSpace(secret)) credentials.Add(new ClientSecretCredential(tenant, id, secret));
            else if (!string.IsNullOrWhiteSpace(certificate)) credentials.Add(new ClientCertificateCredential(tenant, id, certificate));
        }
        credentials.Add(new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = config.TenantId, ManagedIdentityClientId = config.ClientId, ExcludeInteractiveBrowserCredential = false }));
        return new ChainedTokenCredential(credentials.ToArray());
    }

    public async Task<SecretResult> GetSecretAsync(string secretId, SecretType type, CancellationToken ct = default)
    {
        try
        {
            var (name, version) = ParseSecretIdentifier(secretId);
            var result = await client.GetSecretAsync(name, version, ct);
            return new SecretResult { Success = true, Value = result.Value.Value };
        }
        catch (RequestFailedException ex) { return new SecretResult { ErrorMessage = $"Azure Key Vault request failed (HTTP {ex.Status}, {ex.ErrorCode}). Verify secret name and Key Vault access permissions." }; }
        catch (AuthenticationFailedException) { return new SecretResult { ErrorMessage = "Azure authentication failed. Run az login or configure a managed identity/service principal through Azure environment variables." }; }
        catch (ArgumentException ex) { return new SecretResult { ErrorMessage = ex.Message }; }
    }

    public async Task<List<SecretMetadata>> ListSecretsAsync(SecretType? filter = null, CancellationToken ct = default)
    {
        var result = new List<SecretMetadata>();
        await foreach (var property in client.GetPropertiesOfSecretsAsync(ct))
        {
            if (property.Enabled == false) continue;
            SecretType? type = null;
            if (property.Tags.TryGetValue("sshocked-type", out var tagged) && Enum.TryParse<SecretType>(tagged, true, out var parsed)) type = parsed;
            else if (property.ContentType?.Contains("ssh", StringComparison.OrdinalIgnoreCase) == true && property.ContentType.Contains("public", StringComparison.OrdinalIgnoreCase)) type = SecretType.SshPublicKey;
            else if (property.ContentType?.Contains("ssh", StringComparison.OrdinalIgnoreCase) == true || property.ContentType?.Contains("pem", StringComparison.OrdinalIgnoreCase) == true) type = SecretType.SshPrivateKey;
            else if (property.ContentType?.Contains("pkcs12", StringComparison.OrdinalIgnoreCase) == true || property.ContentType?.Contains("pkcs-12", StringComparison.OrdinalIgnoreCase) == true) type = SecretType.Certificate;
            // Untagged secrets remain selectable because Key Vault metadata often has no type.
            if (filter == null || type == null || type == filter) result.Add(new(property.Name, property.Id.ToString(), type, property.ContentType));
        }
        return result;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var enumerator = client.GetPropertiesOfSecretsAsync(ct).GetAsyncEnumerator(ct);
            await enumerator.MoveNextAsync();
            return true;
        }
        catch (RequestFailedException) { return false; }
        catch (AuthenticationFailedException) { return false; }
    }

    public (string Name, string? Version) ParseSecretIdentifier(string secretId)
    {
        if (Uri.TryCreate(secretId, UriKind.Absolute, out var uri))
        {
            if (!uri.Scheme.Equals(vaultUri.Scheme, StringComparison.OrdinalIgnoreCase) || !uri.Authority.Equals(vaultUri.Authority, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Secret URI must belong to the configured Key Vault.");
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 2 or > 3 || parts[0] != "secrets" || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw new ArgumentException("Expected a Key Vault /secrets/<name>[/<version>] URI.");
            ValidateName(parts[1]);
            if (parts.Length == 3) ValidateName(parts[2]);
            return (parts[1], parts.Length == 3 ? parts[2] : null);
        }
        ValidateName(secretId);
        return (secretId, null);
    }

    private static void ValidateName(string name)
    {
        if (!Regex.IsMatch(name, "^[A-Za-z0-9-]{1,127}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Key Vault secret names contain only letters, numbers, and hyphens (1–127 characters).");
    }
}
