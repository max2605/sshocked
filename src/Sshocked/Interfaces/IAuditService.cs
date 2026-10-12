using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IAuditService
{
    void Record(AuditEntry entry);
    IReadOnlyList<AuditEntry> Query(AuditQuery query);
    void Clear();
}
