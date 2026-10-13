using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class ManagedSessionTestLease(ServerHost host) : ISshConnectionLease
{
    public ServerHost Host { get; } = host;
    public IReadOnlyDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>();
    public bool IsDisposed { get; private set; }
    public ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
}
