using System.Diagnostics;
using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class AzureBastionTunnel(Process process, int port) : IBastionTunnel
{
    private readonly (TaskCompletionSource Ready, Task<Exception> Exited) monitor = Monitor(process);
    private int disposed;

    public int LocalPort { get; } = port;
    public bool IsReady => monitor.Ready.Task.IsCompletedSuccessfully && !process.HasExited;

    private static (TaskCompletionSource Ready, Task<Exception> Exited) Monitor(Process process)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new CapturedOutput();
        var stdout = Drain(process.StandardOutput, ready, errors);
        var stderr = Drain(process.StandardError, ready, errors);
        return (ready, ObserveExit(process, stdout, stderr, ready, errors));
    }

    private static async Task Drain(StreamReader reader, TaskCompletionSource ready, CapturedOutput errors)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Contains(AzureCliSyntax.TunnelReadyMarker, StringComparison.OrdinalIgnoreCase) && line.Contains(AzureCliSyntax.Tunnel, StringComparison.OrdinalIgnoreCase)) ready.TrySetResult();
            lock (errors)
            {
                // Keep detecting certificate failures even after the diagnostic buffer is full.
                errors.CertificateFailure |= AzureCliErrors.IsCertificateFailure(line);
                if (errors.Text.Length < 16384) errors.Text.AppendLine(line);
            }
        }
    }

    private static async Task<Exception> ObserveExit(Process process, Task stdout, Task stderr, TaskCompletionSource ready, CapturedOutput errors)
    {
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        lock (errors)
        {
            Exception error = errors.CertificateFailure
                ? new AzureCliCertificateException(AzureCliSyntax.TunnelOperation, process.ExitCode, errors.Text.ToString())
                : new ProcessExecutionException(AzureCliSyntax.TunnelOperation, process.ExitCode, errors.Text.ToString());
            ready.TrySetException(error);
            return error;
        }
    }

    public async Task WaitUntilReady(TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            await monitor.Ready.Task.WaitAsync(timeout, ct);
            if (process.HasExited) throw await monitor.Exited.WaitAsync(timeout, ct);
        }
        catch (TimeoutException ex) { throw new TimeoutException("Azure Bastion tunnel did not become ready. Verify Bastion Standard/Premium SKU, native client support, target routing, and CLI permissions.", ex); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await monitor.Exited;
            if (monitor.Ready.Task.IsFaulted) _ = monitor.Ready.Task.Exception;
        }
        finally { process.Dispose(); }
    }

    private sealed class CapturedOutput
    {
        public StringBuilder Text { get; } = new();
        public bool CertificateFailure { get; set; }
    }
}
