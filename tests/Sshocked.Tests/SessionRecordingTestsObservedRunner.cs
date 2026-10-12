using System.Diagnostics;


using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

internal sealed class SessionRecordingTestsObservedRunner : IExternalProcessRunner
{
    private readonly ExternalProcessRunner inner = new();
    public int ProcessId { get; private set; }
    public bool CloseStandardOutput { get; init; }

    public Process Start(ProcessStartInfo info)
    {
        var process = inner.Start(info);
        ProcessId = process.Id;
        if (CloseStandardOutput)
            process.StandardOutput.Dispose();
        return process;
    }

    public Task<ProcessResult> Run(ProcessStartInfo info, CancellationToken cancellationToken = default) => inner.Run(info, cancellationToken);
}
