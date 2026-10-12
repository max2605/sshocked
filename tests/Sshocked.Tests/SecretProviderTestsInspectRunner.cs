using System.Diagnostics;





using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SecretProviderTestsInspectRunner(Func<ProcessStartInfo, ProcessResult> inspect) : IExternalProcessRunner
{
    public Process Start(ProcessStartInfo startInfo) => throw new NotSupportedException();
    public Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default) => Task.FromResult(inspect(startInfo));
}
