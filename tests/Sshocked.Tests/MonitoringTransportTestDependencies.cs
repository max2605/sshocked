using System.Diagnostics;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class MonitoringTransportTestDependencies : IProcessService, IExternalProcessRunner, ISecretProviderRegistry
{
    public ServerHost PreparedHost { get; set; } = new() { Id = "host", HostName = "host.invalid", User = "operator" };
    public ManagedSessionTestLease? Lease { get; private set; }
    public int Prepares { get; private set; }
    public int Resolves { get; private set; }
    public ProcessStartInfo? Info { get; private set; }
    public string? Input { get; private set; }
    public int Limit { get; private set; }
    public SecretResult Secret { get; set; } = new() { Success = true, Value = "private-value-123" };
    public Func<CancellationToken, Task<ProcessResult>>? Work { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ISshConnectionLease> PrepareConnection(ServerHost host, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Prepares++;
        Lease = new ManagedSessionTestLease(PreparedHost);
        return Task.FromResult<ISshConnectionLease>(Lease);
    }

    public Task<ProcessResult> RunBounded(ProcessStartInfo startInfo, int maximumOutputBytes, string? input = null, CancellationToken cancellationToken = default)
    {
        startInfo.RedirectStandardInput = true;
        Info = startInfo;
        Input = input;
        Limit = maximumOutputBytes;
        Entered.TrySetResult();
        return Work?.Invoke(cancellationToken) ?? Task.FromResult(new ProcessResult(0, "probe-output", ""));
    }

    public Task<SecretResult> Resolve(SecretRef reference, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Resolves++;
        return Task.FromResult(Secret);
    }

    public Task RunSsh(ServerHost host) => throw new InvalidOperationException("Unexpected interactive SSH connection.");
    public Task RunSshInteractive(ServerHost host, string command) => throw new InvalidOperationException("Unexpected interactive SSH command.");
    public Task<string> RunSshCommand(ServerHost host, string command) => throw new InvalidOperationException("Unexpected unbounded SSH command.");
    public Process Start(ProcessStartInfo startInfo) => throw new InvalidOperationException("Unexpected unmanaged process start.");
    public Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Monitoring must use bounded capture.");
    public IReadOnlyList<SecretProviderConfig> GetConfigurations() => [];
    public ISecretProvider GetProvider(string? name = null) => throw new InvalidOperationException("Unexpected provider selection.");
    public void ClearCache() { }
}
