using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

internal sealed class MacroTestDependencies : IProcessService, IAuditService, IConsoleWriterService
{
    public List<string> Commands { get; } = [];
    public List<string> Targets { get; } = [];
    public List<AuditEntry> Events { get; } = [];
    public Queue<string?> Input { get; } = new();
    public List<string> Output { get; } = [];
    public string? FailureHost { get; set; }
    public bool RunLocally { get; set; }
    public string? PreparedPassword { get; set; }
    public int PreparedValidations { get; private set; }
    public bool LastNonInteractive { get; private set; }
    public ManagedSessionTestLease? LastLease { get; private set; }
    public Task RunSsh(ServerHost host) => Task.CompletedTask;
    public Task RunSshInteractive(ServerHost host, string command) => Task.CompletedTask;
    public Task<string> RunSshCommand(ServerHost host, string command) => Task.FromResult("");

    public Task<ProcessResult> RunSshCommandStreamingValidated(ServerHost host, string command,
        Action<ServerHost> validatePrepared, Action<string> output, bool nonInteractive = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        PreparedValidations++;
        LastNonInteractive = nonInteractive;
        validatePrepared(new ServerHost { Id = host.Id, SshPassword = PreparedPassword ?? host.SshPassword, SudoPassword = host.SudoPassword });
        return RunSshCommandStreaming(host, command, output, ct);
    }

    public async Task<ProcessResult> RunSshCommandStreaming(ServerHost host, string command, Action<string> output, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Commands.Add(command); Targets.Add(host.Id);
        var result = RunLocally ? await new ExternalProcessRunner().Run(LocalShellTestRunner.ShellInfo(command), ct)
            : new ProcessResult(host.Id == FailureHost ? 4 : 0, "command output", "");
        output(result.StandardOutput + result.StandardError);
        return result;
    }

    public Task<ISshConnectionLease> PrepareConnection(ServerHost host, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prepared = new ServerHost
        {
            Id = host.Id,
            Alias = host.Alias,
            HostName = host.HostName,
            User = host.User,
            SshPassword = PreparedPassword,
            SudoPassword = host.SudoPassword
        };
        return Task.FromResult<ISshConnectionLease>(LastLease = new(prepared));
    }

    public void Record(AuditEntry entry) => Events.Add(entry);
    public IReadOnlyList<AuditEntry> Query(AuditQuery query) => Events.TakeLast(query.Last).Reverse().ToList();
    public void Clear() => Events.Clear();
    public void WriteLine(string message) => Output.Add(message);
    public void Write(string message) => Output.Add(message);
    public string? ReadLine() => Input.Count > 0 ? Input.Dequeue() : null;
}
