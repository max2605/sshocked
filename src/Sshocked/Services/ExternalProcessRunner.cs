using System.Diagnostics;
using System.Text;
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

    public Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default) => RunCore(startInfo, null, cancellationToken);

    public Task<ProcessResult> RunStreaming(ProcessStartInfo startInfo, Action<string> output, CancellationToken cancellationToken = default)
    {
        var gate = new object();
        return RunCore(startInfo, null, cancellationToken, onOutput: chunk => { lock (gate) output(chunk); });
    }

    public Task<ProcessResult> RunSession(ProcessStartInfo startInfo, Func<Task>? onStarted, CancellationToken cancellationToken = default)
        => RunCore(startInfo, null, cancellationToken, onStarted);

    public Task<ProcessResult> RunWithInput(ProcessStartInfo startInfo, string input, CancellationToken cancellationToken = default)
    {
        startInfo.RedirectStandardInput = true;
        return RunCore(startInfo, input, cancellationToken);
    }

    private async Task<ProcessResult> RunCore(ProcessStartInfo startInfo, string? input, CancellationToken cancellationToken, Func<Task>? onStarted = null, Action<string>? onOutput = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Start(startInfo);
        var output = startInfo.RedirectStandardOutput ? ReadOutput(process.StandardOutput, onOutput, cancellationToken) : Task.FromResult(string.Empty);
        var error = startInfo.RedirectStandardError ? ReadOutput(process.StandardError, onOutput, cancellationToken) : Task.FromResult(string.Empty);
        var inputTask = input is null ? Task.CompletedTask : SendInput(process, input, cancellationToken);
        try
        {
            // A failing output consumer must stop the child even if it is blocked on a full pipe.
            var observerFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ObserveFailure(output, observerFailure);
            ObserveFailure(error, observerFailure);
            if (onStarted is not null) await onStarted();
            var exit = process.WaitForExitAsync(cancellationToken);
            if (await Task.WhenAny(exit, observerFailure.Task) == observerFailure.Task) await observerFailure.Task;
            await exit;
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

    private static void ObserveFailure(Task reader, TaskCompletionSource failure)
    {
        _ = reader.ContinueWith(task => failure.TrySetException(task.Exception!.InnerExceptions),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static async Task<string> ReadOutput(StreamReader reader, Action<string>? onOutput, CancellationToken ct)
    {
        if (onOutput is null) return await reader.ReadToEndAsync(ct);
        // An open remote shell may never fill StreamReader's requested character buffer.
        // Read pipe bytes directly so short output is delivered without waiting for EOF.
        var bytes = new byte[4096];
        var encoding = reader.CurrentEncoding;
        var decoder = encoding.GetDecoder();
        var buffer = new char[encoding.GetMaxCharCount(bytes.Length)];
        var captured = new StringBuilder();
        int count;
        while ((count = await reader.BaseStream.ReadAsync(bytes.AsMemory(), ct)) > 0)
        {
            var characters = decoder.GetChars(bytes.AsSpan(0, count), buffer, flush: false);
            var chunk = new string(buffer, 0, characters);
            captured.Append(chunk);
            onOutput(chunk);
        }
        var trailing = decoder.GetChars(ReadOnlySpan<byte>.Empty, buffer, flush: true);
        if (trailing > 0)
        {
            var chunk = new string(buffer, 0, trailing);
            captured.Append(chunk);
            onOutput(chunk);
        }
        return captured.ToString();
    }

    private static async Task SendInput(Process process, string input, CancellationToken cancellationToken)
    {
        try { await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken); await process.StandardInput.FlushAsync(cancellationToken); }
        catch (IOException) { /* A rejecting command may close stdin before consuming credentials. Its exit code is authoritative. */ }
        finally { process.StandardInput.Close(); }
    }
}
