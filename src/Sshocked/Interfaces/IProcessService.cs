using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IProcessService
{
    Task RunSsh(ServerHost host);
    Task<string> RunSshCommand(ServerHost host, string command);
    Task RunSshInteractive(ServerHost host, string command);
    Task RunAzureBastionSsh(ServerHost host) => RunSsh(host);
    async Task<int> RunSshSession(ServerHost host, string? initialCommand = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (initialCommand == null) await RunSsh(host); else await RunSshInteractive(host, initialCommand);
        return 0;
    }
    async Task<ProcessResult> RunSshCommandResult(ServerHost host, string command, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ProcessResult(0, await RunSshCommand(host, command), "");
    }
    Task<ISshConnectionLease> PrepareConnectionAsync(ServerHost host, CancellationToken ct = default) =>
        throw new NotSupportedException("This process service does not expose prepared SSH connections.");
}
