using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class GroupSessionTestDependencies(params string?[] commands) :
    IGroupConnectionService, IContainerGroupService, IProcessService, IConsoleWriterService
{
    private readonly Queue<string?> input = new(commands);
    public List<string> Calls { get; } = [];
    public List<string> Writes { get; } = [];
    public string? FailureHost { get; init; }
    public TaskCompletionSource? CommandGate { get; init; }

    public Task ConnectAllSequential(List<ServerHost> hosts) => Record("server:sequence");
    public Task ConnectAllMultiTab(List<ServerHost> hosts) => Record("server:tabs");
    public Task ExecAllSequential(AppConfig config, List<ContainerModel> containers) => Record("container:sequence");
    public Task ExecAllMultiTab(AppConfig config, List<ContainerModel> containers) => Record("container:tabs");
    public Task RunCommandOnAll(AppConfig config, List<ContainerModel> containers, string command) => Record($"container-command:{command}");
    public ServerHost? FindParentServer(AppConfig config, ContainerModel container) => null;
    public Task RunSsh(ServerHost host) => Task.CompletedTask;
    public Task RunSshInteractive(ServerHost host, string command) => Task.CompletedTask;

    public Task<string> RunSshCommand(ServerHost host, string command)
    {
        Calls.Add($"command:{host.Alias}:{command}");
        return host.Alias == FailureHost ? Task.FromException<string>(new IOException("Host unavailable")) : Task.FromResult("output");
    }

    public async Task<ProcessResult> RunSshCommandStreaming(ServerHost host, string command, Action<string> output, CancellationToken ct = default)
    {
        Calls.Add($"command:{host.Alias}:{command}");
        if (host.Alias == FailureHost) throw new IOException("Host unavailable");
        output("live output\n");
        if (CommandGate is not null) await CommandGate.Task.WaitAsync(ct);
        return new(0, "buffered output", "");
    }

    public void WriteLine(string message) { }
    public void Write(string message) => Writes.Add(message);
    public string? ReadLine() => input.TryDequeue(out var next) ? next : null;

    private Task Record(string call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }
}
