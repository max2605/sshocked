using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SshRunnerLifecycleTestsAudit(List<string> events) : IAuditService
{
    public List<AuditEntry> Entries { get; } = [];

    public void Record(AuditEntry entry)
    {
        events.Add("audit:" + entry.EventType);
        Entries.Add(entry);
    }

    public IReadOnlyList<AuditEntry> Query(AuditQuery query) => Entries;
    public void Clear() => Entries.Clear();
}
