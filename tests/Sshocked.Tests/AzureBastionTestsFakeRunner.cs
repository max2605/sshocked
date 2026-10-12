using System.Diagnostics;

using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class AzureBastionTestsFakeRunner : IExternalProcessRunner
{
    public Queue<ProcessResult> Results { get; } = new();
    public List<ProcessStartInfo> Starts { get; } = [];

    public Process Start(ProcessStartInfo startInfo) => throw new NotSupportedException();
    public Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        Starts.Add(startInfo);
        return Task.FromResult(Results.Dequeue());
    }
}
