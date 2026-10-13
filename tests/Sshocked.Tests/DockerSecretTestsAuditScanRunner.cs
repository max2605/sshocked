using System.Diagnostics;



using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class DockerSecretTestsAuditScanRunner : IExternalProcessRunner
{
    public List<string> Commands { get; } = [];
    public List<string> Inputs { get; } = [];
    public List<ProcessStartInfo> StartInfos { get; } = [];
    public List<int> OutputLimits { get; } = [];

    public Process Start(ProcessStartInfo startInfo) => throw new NotSupportedException();
    public Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Docker capture must be bounded.");

    public Task<ProcessResult> RunBounded(ProcessStartInfo startInfo, int maximumOutputBytes, string? input = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartInfos.Add(startInfo);
        OutputLimits.Add(maximumOutputBytes);
        if (input is not null) Inputs.Add(input);
        var command = startInfo.ArgumentList[^1];
        Commands.Add(command);
        var output = command.Contains("docker info", StringComparison.Ordinal) ? "{\"ServerVersion\":\"27.0.0\"}" : "";
        return Task.FromResult(new ProcessResult(0, output, ""));
    }

    public Task<ProcessResult> RunWithInput(ProcessStartInfo startInfo, string input, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Docker protected capture must be bounded.");
}
