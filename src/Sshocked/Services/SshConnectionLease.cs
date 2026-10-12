using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class SshConnectionLease(ServerHost host) : ISshConnectionLease
{
    private readonly List<IAsyncDisposable> resources = [];

    public ServerHost Host { get; } = host;
    public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string?> Environment => Variables;

    public void Add(IAsyncDisposable resource) => resources.Add(resource);

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        for (var i = resources.Count - 1; i >= 0; i--)
        {
            try { await resources[i].DisposeAsync(); }
            catch (Exception error) { failures.Add(error); }
        }
        resources.Clear(); Variables.Clear();
        if (failures.Count > 0) throw new AggregateException("SSH connection cleanup failed.", failures);
    }
}
