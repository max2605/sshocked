namespace Sshocked.Services;

public static class ServerFormLabels
{
    public const string NoGroup = "(none)";
    public const string SshKey = "SSH Key (Default)";
    public const string Password = "Password";
    public const string SshAgent = "SSH Agent";
    public const string CustomOptions = "Custom SSH Options";
    public const string AzureBastion = "Azure Bastion";
    public const string KeyVault = "From Key Vault";

    public static readonly string[] AuthenticationMethods = [SshKey, Password, SshAgent, CustomOptions, AzureBastion, KeyVault];
}
