namespace Sshocked.Models;

public sealed class DiagnosticReport
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ApplicationVersion { get; set; } = typeof(DiagnosticReport).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    public string Platform { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Runtime { get; set; } = string.Empty;
    public bool IncludesPaths { get; set; }
    public bool IncludesCommandText { get; set; }
    public DiagnosticHost? Host { get; set; }
    public List<DiagnosticTool> Tools { get; set; } = [];
    public List<DiagnosticCheck> Checks { get; set; } = [];
    public List<DiagnosticFailure> Failures { get; set; } = [];
}
