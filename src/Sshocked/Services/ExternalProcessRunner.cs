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

    public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default) => RunCore(startInfo, null, cancellationToken);

    public Task<ProcessResult> RunSessionAsync(ProcessStartInfo startInfo, Func<Task>? onStarted, CancellationToken cancellationToken = default)
        => RunCore(startInfo, null, cancellationToken, onStarted);

    public Task<ProcessResult> RunWithInputAsync(ProcessStartInfo startInfo, string input, CancellationToken cancellationToken = default)
    {
        startInfo.RedirectStandardInput = true;
        return RunCore(startInfo, input, cancellationToken);
    }

    private async Task<ProcessResult> RunCore(ProcessStartInfo startInfo, string? input, CancellationToken cancellationToken, Func<Task>? onStarted = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Start(startInfo);
        var output = startInfo.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync(cancellationToken) : Task.FromResult(string.Empty);
        var error = startInfo.RedirectStandardError ? process.StandardError.ReadToEndAsync(cancellationToken) : Task.FromResult(string.Empty);
        var inputTask = input is null ? Task.CompletedTask : SendInput(process, input, cancellationToken);
        try
        {
            if (onStarted is not null) await onStarted();
            await process.WaitForExitAsync(cancellationToken);
            await inputTask;
            return new ProcessResult(process.ExitCode, await output, await error);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync(CancellationToken.None);
            // Observe redirected readers even when cancellation interrupts the process.
            try { await Task.WhenAll(output, error, inputTask); }
            catch { /* Observe pipe errors caused by shutdown without replacing the initiating failure. */ }
            throw;
        }
    }

    private static async Task SendInput(Process process, string input, CancellationToken cancellationToken)
    {
        try { await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken); await process.StandardInput.FlushAsync(cancellationToken); }
        catch (IOException) { /* A rejecting command may close stdin before consuming credentials. Its exit code is authoritative. */ }
        finally { process.StandardInput.Close(); }
    }
}
