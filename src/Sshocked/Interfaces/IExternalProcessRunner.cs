using System.Diagnostics;
using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IExternalProcessRunner
{
    Process Start(ProcessStartInfo startInfo);
    Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default);
    async Task<ProcessResult> RunStreaming(ProcessStartInfo startInfo, Action<string> output, CancellationToken cancellationToken = default)
    {
        var result = await Run(startInfo, cancellationToken);
        if (result.StandardOutput.Length > 0) output(result.StandardOutput);
        if (result.StandardError.Length > 0) output(result.StandardError);
        return result;
    }
    Task<ProcessResult> RunSession(ProcessStartInfo startInfo, Func<Task>? onStarted, CancellationToken cancellationToken = default)
        => onStarted is null ? Run(startInfo, cancellationToken)
            : throw new NotSupportedException("This runner does not expose session-start callbacks.");
    Task<ProcessResult> RunWithInput(ProcessStartInfo startInfo, string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This runner does not support protected standard input.");
}
