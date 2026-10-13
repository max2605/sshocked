using System.Collections.Concurrent;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class DockerOperationsTestsProcess(Func<ServerHost, string, CancellationToken, Task<ProcessResult>> execute) : IProcessService
{
    public ConcurrentQueue<string> Commands { get; } = new();
    public ConcurrentQueue<string> Inputs { get; } = new();

    public async Task<ProcessResult> RunSshCommandResultNonInteractive(ServerHost host, string command, CancellationToken ct = default)
    {
        Commands.Enqueue(command);
        return await execute(host, command, ct);
    }

    public async Task<ProcessResult> RunSshCommandWithInputNonInteractive(ServerHost host, string command, string input, CancellationToken ct = default)
    {
        Inputs.Enqueue(input);
        return await RunSshCommandResultNonInteractive(host, command, ct);
    }

    public Task<ProcessResult> RunSshCommandResult(ServerHost host, string command, CancellationToken ct = default) => throw new InvalidOperationException("Captured commands must use bounded noninteractive execution.");
    public Task<ProcessResult> RunSshCommandWithInput(ServerHost host, string command, string input, CancellationToken ct = default) => throw new InvalidOperationException("Protected captured commands must use bounded noninteractive execution.");
    public Task<string> RunSshCommand(ServerHost host, string command) => throw new InvalidOperationException("Captured commands must use bounded noninteractive execution.");

    public Task RunSsh(ServerHost host) => throw new NotSupportedException();
    public Task RunSshInteractive(ServerHost host, string command) => throw new NotSupportedException();
}
