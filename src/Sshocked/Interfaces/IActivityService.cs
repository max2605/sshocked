using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IActivityService
{
    IReadOnlyList<AuditEntry> Events(AuditQuery query);
    IReadOnlyList<SessionMetadata> Recordings(int last = 20, string? server = null, DateTimeOffset? since = null);
    string Status();
}
