using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IProcessService
{
    Task RunSsh(ServerHost host);
    Task<string> RunSshCommand(ServerHost host, string command);
    Task<ProcessResult> RunSshCommandResultNonInteractive(ServerHost host, string command, CancellationToken ct = default)
        => throw new NotSupportedException("This process service does not support noninteractive batch commands.");
    Task<ProcessResult> RunSshCommandStreamingValidated(ServerHost host, string command,
        Action<ServerHost> validatePrepared, Action<string> output, bool nonInteractive = false, CancellationToken ct = default)
    {
        if (nonInteractive) throw new NotSupportedException("This process service does not support validated noninteractive commands.");
        validatePrepared(host);
        return RunSshCommandStreaming(host, command, output, ct);
    }
    async Task<ProcessResult> RunSshCommandStreaming(ServerHost host, string command, Action<string> output, CancellationToken ct = default)
    {
        var result = await RunSshCommandResult(host, command, ct);
        if (result.StandardOutput.Length > 0) output(result.StandardOutput);
        if (result.StandardError.Length > 0) output(result.StandardError);
        return result;
    }
    Task RunSshInteractive(ServerHost host, string command);
    async Task<int> RunSshSession(ServerHost host, string? initialCommand = null, CancellationToken ct = default, Func<Task>? onStarted = null, bool auditCommand = true)
    {
        ct.ThrowIfCancellationRequested();
        if (onStarted is not null) throw new NotSupportedException("This process service does not expose session-start callbacks.");
        if (initialCommand == null) await RunSsh(host); else await RunSshInteractive(host, initialCommand);
        return 0;
    }
    async Task<ProcessResult> RunSshCommandResult(ServerHost host, string command, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ProcessResult(0, await RunSshCommand(host, command), "");
    }
    Task<ISshConnectionLease> PrepareConnection(ServerHost host, CancellationToken ct = default) =>
        throw new NotSupportedException("This process service does not expose prepared SSH connections.");
    Task<ProcessResult> RunSshCommandWithInput(ServerHost host, string command, string input, CancellationToken ct = default) =>
        throw new NotSupportedException("This process service does not support protected standard input.");
    Task<ProcessResult> RunSshCommandWithInputNonInteractive(ServerHost host, string command, string input, CancellationToken ct = default) =>
        throw new NotSupportedException("This process service does not support bounded noninteractive protected standard input.");
}
