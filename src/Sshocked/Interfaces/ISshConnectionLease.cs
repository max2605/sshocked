using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISshConnectionLease : IAsyncDisposable
{
    ServerHost Host { get; }
    IReadOnlyDictionary<string, string?> Environment { get; }
}
