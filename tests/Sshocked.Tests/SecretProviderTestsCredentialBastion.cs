using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SecretProviderTestsCredentialBastion(BastionAuthType expectedAuth) : IBastionService
{
    public SecretProviderTestsCredentialTunnel Tunnel { get; } = new();
    public string? KeyPath { get; private set; }

    public Task<IBastionTunnel> CreateTunnel(AzureBastionConfig config, CancellationToken ct = default)
    {
        Assert.Equal(expectedAuth, config.AuthType);
        KeyPath = config.SshKeyPath;
        return Task.FromResult<IBastionTunnel>(Tunnel);
    }

    public Task<BastionCliInfo> CheckPrerequisites(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<BastionVmInfo>> ListTargetVms(string bastionName, string resourceGroup, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> RunNativeSsh(ServerHost host, string? command = null, CancellationToken ct = default, IReadOnlyDictionary<string, string?>? environment = null) => throw new InvalidOperationException("Effective vault key/password credentials must use a checked tunnel command.");
    public Task<ProcessResult> RunNativeCommand(ServerHost host, string command, CancellationToken ct = default, IReadOnlyDictionary<string, string?>? environment = null) => throw new InvalidOperationException("Effective vault key/password credentials must use a checked tunnel command.");
}
