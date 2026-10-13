using System.Diagnostics;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class DiagnosticToolTestRunner : IExternalProcessRunner
{
    public List<ProcessStartInfo> Calls { get; } = [];
    public int MaximumBytes { get; private set; }
    public bool Timeout { get; set; }
    public Process Start(ProcessStartInfo info) => throw new InvalidOperationException("Diagnostics must not launch an interactive process.");
    public Task<ProcessResult> Run(ProcessStartInfo info, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Diagnostic output must be bounded.");
    public Task<ProcessResult> RunBounded(ProcessStartInfo info, int maximumOutputBytes, string? input = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(info);
        MaximumBytes = maximumOutputBytes;
        if (Timeout) throw new OperationCanceledException();
        return Task.FromResult(info.ArgumentList.Contains("-V") ? new ProcessResult(0, "", "OpenSSH_9.8\n")
            : new ProcessResult(0, "{\"azure-cli\":\"2.80.0\"}", ""));
    }
}
