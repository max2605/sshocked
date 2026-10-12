using System.Diagnostics;
using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IExternalProcessRunner
{
    Process Start(ProcessStartInfo startInfo);
    Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default);
    Task<ProcessResult> RunSession(ProcessStartInfo startInfo, Func<Task>? onStarted, CancellationToken cancellationToken = default)
        => onStarted is null ? Run(startInfo, cancellationToken)
            : throw new NotSupportedException("This runner does not expose session-start callbacks.");
    Task<ProcessResult> RunWithInput(ProcessStartInfo startInfo, string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This runner does not support protected standard input.");
}
