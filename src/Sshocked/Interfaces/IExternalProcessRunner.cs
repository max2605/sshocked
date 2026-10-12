using System.Diagnostics;
using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IExternalProcessRunner
{
    Process Start(ProcessStartInfo startInfo);
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default);
}
