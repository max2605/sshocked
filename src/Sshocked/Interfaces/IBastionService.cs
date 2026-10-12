using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IBastionService
{
    Task<IBastionTunnel> CreateTunnelAsync(AzureBastionConfig config, CancellationToken ct = default);
    Task<BastionCliInfo> CheckPrerequisitesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BastionVmInfo>> ListTargetVmsAsync(string bastionName, string resourceGroup, CancellationToken ct = default);
    Task<int> RunNativeSshAsync(ServerHost host, string? command = null, CancellationToken ct = default, IReadOnlyDictionary<string, string?>? environment = null);
    Task<int> RunNativeSessionAsync(ServerHost host, string? command, CancellationToken ct, IReadOnlyDictionary<string, string?>? environment, Func<Task>? onStarted)
        => onStarted is null ? RunNativeSshAsync(host, command, ct, environment)
            : throw new NotSupportedException("This Bastion service does not expose session-start callbacks.");
    Task<ProcessResult> RunNativeCommandAsync(ServerHost host, string command, CancellationToken ct = default, IReadOnlyDictionary<string, string?>? environment = null);
}

public interface IBastionTunnel : IAsyncDisposable
{
    int LocalPort { get; }
    bool IsReady { get; }
    Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken ct = default);
}

public sealed record BastionCliInfo(bool IsInstalled, Version? CliVersion, bool HasBastionExtension, bool HasSshExtension, bool IsLoggedIn, string? ErrorMessage);
public sealed record BastionVmInfo(string Name, string ResourceId, string? PrivateIp, string? OsType);
