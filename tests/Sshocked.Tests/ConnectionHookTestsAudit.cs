using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class ConnectionHookTestsAudit : IAuditService
{
    public List<AuditEntry> Entries { get; } = [];
    public TaskCompletionSource Recorded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Record(AuditEntry entry)
    {
        Entries.Add(entry);
        Recorded.TrySetResult();
    }

    public IReadOnlyList<AuditEntry> Query(AuditQuery query) => Entries;
    public void Clear() => Entries.Clear();
}
