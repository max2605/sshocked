namespace Sshocked.Services;

internal static class AzureCliSyntax
{
    public const string Network = "network";
    public const string Bastion = "bastion";
    public const string Ssh = "ssh";
    public const string Tunnel = "tunnel";
    public const string VersionCommand = "version";
    public const string Extension = "extension";
    public const string List = "list";
    public const string Account = "account";
    public const string Show = "show";
    public const string Vm = "vm";
    public const string OutputOption = "--output";
    public const string JsonOutput = "json";
    public const string TsvOutput = "tsv";
    public const string NameOption = "--name";
    public const string ResourceGroupOption = "--resource-group";
    public const string ShowDetailsOption = "--show-details";
    public const string QueryOption = "--query";
    public const string TenantIdQuery = "tenantId";
    public const string SubscriptionOption = "--subscription";
    public const string AuthTypeOption = "--auth-type";
    public const string AadAuthentication = "AAD";
    public const string PasswordAuthentication = "password";
    public const string KeyAuthentication = "ssh-key";
    public const string UsernameOption = "--username";
    public const string SshKeyOption = "--ssh-key";
    public const string PortOption = "--port";
    public const string ResourcePortOption = "--resource-port";
    public const string TargetResourceIdOption = "--target-resource-id";
    public const string TargetIpOption = "--target-ip-address";
    public const string NameProperty = "name";
    public const string IdProperty = "id";
    public const string CliVersionProperty = "azure-cli";
    public const string StorageProfileProperty = "storageProfile";
    public const string OsDiskProperty = "osDisk";
    public const string OsTypeProperty = "osType";
    public const string PrivateIpsProperty = "privateIps";
    public const string TunnelReadyMarker = "ready";
    public const string TunnelOperation = "Azure Bastion tunnel";
    public const string TargetResourcePattern = @"^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\.Compute/virtualMachines/[^/]+$";
}
