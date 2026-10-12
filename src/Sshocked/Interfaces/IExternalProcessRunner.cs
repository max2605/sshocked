using System.Diagnostics;
using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IExternalProcessRunner
{
    Process Start(ProcessStartInfo startInfo);
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default);
    Task<ProcessResult> RunSessionAsync(ProcessStartInfo startInfo, Func<Task>? onStarted, CancellationToken cancellationToken = default)
        => onStarted is null ? RunAsync(startInfo, cancellationToken)
            : throw new NotSupportedException("This runner does not expose session-start callbacks.");
    Task<ProcessResult> RunWithInputAsync(ProcessStartInfo startInfo, string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This runner does not support protected standard input.");
}
