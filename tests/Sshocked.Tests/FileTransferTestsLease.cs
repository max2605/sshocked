using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class FileTransferTestsLease(ServerHost host) : ISshConnectionLease
{
    public ServerHost Host => host;
    public IReadOnlyDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>
    {
        ["SSH_ASKPASS"] = "lease-askpass"
    };
    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
