namespace Sshocked.Services;

internal static class AzureKeyVaultProviderConstants
{
    public const string ProviderType = "AzureKeyVault";
    public const string SecretTypeTag = "sshocked-type";
    public const string SecretPath = "secrets";
    public const string TenantEnvironment = "AZURE_TENANT_ID";
    public const string ClientIdEnvironment = "AZURE_CLIENT_ID";
    public const string ClientSecretEnvironment = "AZURE_CLIENT_SECRET";
    public const string ClientCertificateEnvironment = "AZURE_CLIENT_CERTIFICATE_PATH";
}
