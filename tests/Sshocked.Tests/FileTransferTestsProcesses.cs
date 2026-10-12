using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class FileTransferTestsProcesses : IProcessService
{
    public List<FileTransferTestsLease> Leases { get; } = [];

    public Task RunSsh(ServerHost host) => throw new NotSupportedException();
    public Task<string> RunSshCommand(ServerHost host, string command) => throw new NotSupportedException();
    public Task RunSshInteractive(ServerHost host, string command) => throw new NotSupportedException();
    public Task<ISshConnectionLease> PrepareConnection(ServerHost host, CancellationToken ct = default)
    {
        FileTransferTestsLease lease = new FileTransferTestsLease(host);
        Leases.Add(lease);
        return Task.FromResult<ISshConnectionLease>(lease);
    }
}
