using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IDiagnosticToolService
{
    Task<IReadOnlyList<DiagnosticTool>> Inspect(bool includePaths, CancellationToken cancellationToken = default);
}
