namespace Sshocked.Models;

public sealed record DiagnosticFailure(DateTimeOffset TimestampUtc, string Category, string EventType,
    int? ExitCode, string? Detail, string? Action);
