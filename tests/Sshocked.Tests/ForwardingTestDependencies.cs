using System.Diagnostics;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class ForwardingTestDependencies : IConfigRepository, IAuditService, IProcessService,
    IForwardingProcessFactory, IConsoleWriterService
{
    public AppConfig Config { get; } = new() { Hosts = [new() { Id = "server", Alias = "server", HostName = "literal.example", User = "tester" }] };
    public List<ManagedSessionTestLease> Leases { get; } = [];
    public List<ForwardingFakeProcess> Processes { get; } = [];
    public List<AuditEntry> Events { get; } = [];
    public List<string> Output { get; } = [];
    public List<ProcessStartInfo> Starts { get; } = [];
    public Func<ForwardingFakeProcess> CreateProcess { get; set; } = () => new();
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AppConfig Load() => Config;
    public void Save(AppConfig config) => throw new NotSupportedException();
    public void Update(Action<AppConfig> action) => action(Config);
    public void Record(AuditEntry entry) => Events.Add(entry);
    public IReadOnlyList<AuditEntry> Query(AuditQuery query) => Events;
    public void Clear() => Events.Clear();
    public Task RunSsh(ServerHost host) => throw new NotSupportedException();
    public Task<string> RunSshCommand(ServerHost host, string command) => throw new NotSupportedException();
    public Task RunSshInteractive(ServerHost host, string command) => throw new NotSupportedException();
    public Task<ISshConnectionLease> PrepareConnection(ServerHost host, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lease = new ManagedSessionTestLease(host);
        Leases.Add(lease);
        return Task.FromResult<ISshConnectionLease>(lease);
    }
    public IForwardingProcess Start(ProcessStartInfo info, ForwardingProfile profile)
    {
        Starts.Add(info);
        var process = CreateProcess();
        Processes.Add(process);
        Started.TrySetResult();
        return process;
    }
    public void WriteLine(string message) => Output.Add(message);
    public void Write(string message) => Output.Add(message);
    public string? ReadLine() => null;
}
