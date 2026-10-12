using System.Diagnostics;
using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class AzureBastionTunnel(Process process, int port) : IBastionTunnel
{
    private readonly (TaskCompletionSource Ready, Task Exited) monitor = Monitor(process);
    private int disposed;

    public int LocalPort { get; } = port;
    public bool IsReady => monitor.Ready.Task.IsCompletedSuccessfully && !process.HasExited;

    private static (TaskCompletionSource Ready, Task Exited) Monitor(Process process)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new StringBuilder();
        var stdout = DrainAsync(process.StandardOutput, ready, errors);
        var stderr = DrainAsync(process.StandardError, ready, errors);
        return (ready, ObserveExitAsync(process, stdout, stderr, ready, errors));
    }

    private static async Task DrainAsync(StreamReader reader, TaskCompletionSource ready, StringBuilder errors)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Contains(AzureCliSyntax.TunnelReadyMarker, StringComparison.OrdinalIgnoreCase) && line.Contains(AzureCliSyntax.Tunnel, StringComparison.OrdinalIgnoreCase)) ready.TrySetResult();
            lock (errors) { if (errors.Length < 16384) errors.AppendLine(line); }
        }
    }

    private static async Task ObserveExitAsync(Process process, Task stdout, Task stderr, TaskCompletionSource ready, StringBuilder errors)
    {
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        lock (errors) ready.TrySetException(new ProcessExecutionException(AzureCliSyntax.TunnelOperation, process.ExitCode, errors.ToString()));
    }

    public async Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        try { await monitor.Ready.Task.WaitAsync(timeout, ct); }
        catch (TimeoutException ex) { throw new TimeoutException("Azure Bastion tunnel did not become ready. Verify Bastion Standard/Premium SKU, native client support, target routing, and CLI permissions.", ex); }
        if (process.HasExited) throw new ProcessExecutionException(AzureCliSyntax.TunnelOperation, process.ExitCode, "Tunnel exited before SSH could connect.");
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
}
