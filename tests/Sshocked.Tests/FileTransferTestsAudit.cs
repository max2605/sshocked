using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class FileTransferTestsAudit : IAuditService
{
    public List<AuditEntry> Entries { get; } = [];

    public void Record(AuditEntry entry) => Entries.Add(entry);
    public IReadOnlyList<AuditEntry> Query(AuditQuery query) => Entries;
    public void Clear() => Entries.Clear();
}
