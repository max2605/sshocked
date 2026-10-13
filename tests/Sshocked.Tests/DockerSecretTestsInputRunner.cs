using System.Diagnostics;



using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class DockerSecretTestsInputRunner : IExternalProcessRunner
{
    public ProcessStartInfo? Info { get; private set; }
    public string? Input { get; private set; }
    public int? OutputLimit { get; private set; }

    public Process Start(ProcessStartInfo startInfo) => throw new NotSupportedException();
    public Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProcessResult> RunWithInput(ProcessStartInfo startInfo, string input, CancellationToken cancellationToken = default)
    {
        Info = startInfo;
        Input = input;
        return Task.FromResult(new ProcessResult(0, "{}", ""));
    }

    public Task<ProcessResult> RunBounded(ProcessStartInfo startInfo, int maximumOutputBytes, string? input = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Info = startInfo;
        Input = input;
        OutputLimit = maximumOutputBytes;
        return Task.FromResult(new ProcessResult(0, "{}", ""));
    }
}
