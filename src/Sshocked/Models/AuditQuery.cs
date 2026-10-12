namespace Sshocked.Models;

public sealed record AuditQuery(int Last = 20, string? Type = null, string? Server = null, DateTimeOffset? Since = null, DateTimeOffset? Until = null);
