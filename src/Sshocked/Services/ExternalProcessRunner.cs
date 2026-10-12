using System.Diagnostics;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    public Process Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
            return process;
        }
        catch { process.Dispose(); throw; }
    }

    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Start(startInfo);
        var output = startInfo.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync(cancellationToken) : Task.FromResult(string.Empty);
        var error = startInfo.RedirectStandardError ? process.StandardError.ReadToEndAsync(cancellationToken) : Task.FromResult(string.Empty);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, await output, await error);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync(CancellationToken.None);
            // Observe redirected readers even when cancellation interrupts the process.
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
            throw;
        }
    }
}
