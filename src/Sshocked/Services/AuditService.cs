using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class AuditService(IConfigRepository repository, ILogger<AuditService> logger, string? path = null) : IAuditService, IDisposable
{
    private readonly AuditStore _store = AuditStore.Create(repository, logger, path ?? Path.Combine(ConfigPaths.DirectoryPath, "audit", "audit.jsonl"));
    public void Record(AuditEntry entry) => _store.Record(entry);
    public IReadOnlyList<AuditEntry> Query(AuditQuery query) => _store.Query(query);
    public void Clear() => _store.Clear();
    public void Dispose() => _store.Dispose();
}
