using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class SshConnectionPreparer(IBastionService bastionService, ISecretProviderRegistry secrets, string? applicationExecutable = null)
{
    public async Task<ISshConnectionLease> Prepare(ServerHost host, bool useTunnel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (host.AuthType == AuthType.AzureBastion && host.AzureBastion == null) throw new ArgumentException("Azure Bastion configuration is missing.");
        var prepared = CopyConnectionHost(host);
        var lease = new SshConnectionLease(prepared);
        try
        {
            await PrepareCredentials(host, lease, ct);
            if (useTunnel && prepared.AzureBastion != null) await PrepareTunnel(lease, ct);
            return lease;
        }
        catch { await lease.DisposeAsync(); throw; }
    }

    private async Task PrepareCredentials(ServerHost host, SshConnectionLease lease, CancellationToken ct)
    {
        var prepared = lease.Host;
        if (host.KeySecretRef != null)
        {
            if (host.KeySecretRef.SecretType is not (SecretType.SshPrivateKey or SecretType.Certificate)) throw new ArgumentException("SSH key reference must use SshPrivateKey or Certificate.");
            var value = await ResolveValue(host.KeySecretRef, ct);
            var keyFile = await SecretKeyFile.Create(value, host.KeySecretRef.SecretType, ct);
            lease.Add(keyFile);
            prepared.IdentityFile = keyFile.Path;
            prepared.AuthType = AuthType.SshKey;
            if (prepared.AzureBastion != null) { prepared.AzureBastion.SshKeyPath = keyFile.Path; prepared.AzureBastion.AuthType = BastionAuthType.SshKey; }
        }
        if (host.PasswordSecretRef != null)
        {
            if (host.PasswordSecretRef.SecretType != SecretType.Password) throw new ArgumentException("SSH password reference must use Password.");
            SshAskPass.Configure(lease.Variables, await ResolveValue(host.PasswordSecretRef, ct), applicationExecutable ?? Environment.ProcessPath);
            prepared.AuthType = AuthType.Password;
            if (prepared.AzureBastion != null) prepared.AzureBastion.AuthType = BastionAuthType.Password;
        }
        else if (!string.IsNullOrEmpty(host.SshPassword)) SshAskPass.Configure(lease.Variables, host.SshPassword, applicationExecutable ?? Environment.ProcessPath);
    }

    private async Task PrepareTunnel(SshConnectionLease lease, CancellationToken ct)
    {
        var prepared = lease.Host;
        var config = prepared.AzureBastion!;
        var tunnel = await bastionService.CreateTunnel(config, ct);
        lease.Add(tunnel);
        await tunnel.WaitUntilReady(TimeSpan.FromSeconds(30), ct);
        prepared.HostName = SshConnectionConstants.LoopbackAddress;
        prepared.ProxyJump = null;
        prepared.Port = tunnel.LocalPort;
        prepared.User = config.Username ?? prepared.User;
        prepared.AuthType = config.AuthType == BastionAuthType.Password ? AuthType.Password : AuthType.SshKey;
        prepared.IdentityFile = config.SshKeyPath ?? prepared.IdentityFile;
        // Host-key verification must follow the VM, not an ephemeral port.
        prepared.SshHostKeyAlias = SshConnectionConstants.BastionHostKeyPrefix + (config.TargetResourceId ?? config.TargetIpAddress);
        prepared.AzureBastion = null;
    }

    private async Task<string> ResolveValue(SecretRef reference, CancellationToken ct)
    {
        var result = await secrets.Resolve(reference, ct);
        if (!result.Success || result.Value == null) throw new InvalidOperationException(result.ErrorMessage ?? "Secret provider returned no value.");
        return result.Value;
    }

    private static ServerHost CopyConnectionHost(ServerHost host) => new()
    {
        Id = host.Id,
        Alias = host.Alias,
        HostName = host.HostName,
        Port = host.Port,
        User = host.User,
        AuthType = host.AuthType,
        IdentityFile = host.IdentityFile,
        CustomSshOptions = host.CustomSshOptions,
        SshHostKeyAlias = host.SshHostKeyAlias,
        ProxyJump = host.ProxyJump,
        AzureBastion = host.AzureBastion is not { } bastion ? null : new AzureBastionConfig
        {
            BastionName = bastion.BastionName,
            BastionResourceGroup = bastion.BastionResourceGroup,
            TargetResourceId = bastion.TargetResourceId,
            TargetIpAddress = bastion.TargetIpAddress,
            TargetPort = bastion.TargetPort,
            LocalPort = bastion.LocalPort,
            AuthType = bastion.AuthType,
            Username = bastion.Username,
            SshKeyPath = bastion.SshKeyPath,
            SubscriptionId = bastion.SubscriptionId,
            TenantId = bastion.TenantId,
            ConnectionMode = bastion.ConnectionMode
        }
    };
}
