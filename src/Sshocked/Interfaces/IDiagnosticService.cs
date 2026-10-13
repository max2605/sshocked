using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IDiagnosticService
{
    Task<DiagnosticReport> Create(string? host, DiagnosticOptions options, CancellationToken cancellationToken = default);
    void Export(DiagnosticReport report, string path);
}
