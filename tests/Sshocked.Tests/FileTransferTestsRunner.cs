using System.Diagnostics;

using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class FileTransferTestsRunner : IExternalProcessRunner
{
    public List<ProcessStartInfo> Calls { get; } = [];
    public Func<CancellationToken, Task<ProcessResult>> Handler { get; set; } = _ => Task.FromResult(new ProcessResult(0, "", ""));

    public Process Start(ProcessStartInfo info) => throw new NotSupportedException();
    public Task<ProcessResult> Run(ProcessStartInfo info, CancellationToken cancellationToken = default)
    {
        Calls.Add(info);
        return Handler(cancellationToken);
    }
}
