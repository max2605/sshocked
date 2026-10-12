using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IBastionService
{
    Task<IBastionTunnel> CreateTunnel(AzureBastionConfig config, CancellationToken ct = default);
    Task<BastionCliInfo> CheckPrerequisites(CancellationToken ct = default);
    Task<IReadOnlyList<BastionVmInfo>> ListTargetVms(string bastionName, string resourceGroup, CancellationToken ct = default);
    Task<int> RunNativeSsh(ServerHost host, string? command = null, CancellationToken ct = default, IReadOnlyDictionary<string, string?>? environment = null);
    Task<int> RunNativeSession(ServerHost host, string? command, CancellationToken ct, IReadOnlyDictionary<string, string?>? environment, Func<Task>? onStarted) => onStarted is null ? RunNativeSsh(host, command, ct, environment) : throw new NotSupportedException("This Bastion service does not expose session-start callbacks.");
    Task<ProcessResult> RunNativeCommand(ServerHost host, string command, CancellationToken ct = default, IReadOnlyDictionary<string, string?>? environment = null);
}
