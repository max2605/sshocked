using System.Diagnostics;
using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ForwardingProcess : IForwardingProcess
{
    private readonly Process _process;
    private readonly ForwardingKind _kind;
    private readonly int _port;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _readers = new();
    private bool _authenticated;
    private bool _listener;
    private bool _listenerCandidate;
    private int _bytes;
    private bool _knownFailure;
    private string _failure = "SSH forwarding ended before authentication and listener setup completed.";
    public int Id => _process.Id;
    public int? ExitCode { get; private set; }
    public string? Error { get { lock (_gate) return _knownFailure ? _failure : null; } }
    public Task Completion { get; }

    public ForwardingProcess(Process process, ForwardingKind kind, int port)
    {
        _process = process;
        _kind = kind;
        _port = port;
        _process.StandardInput.Close();
        var output = Drain(_process.StandardOutput, false);
        var error = Drain(_process.StandardError, true);
        Completion = Observe(output, error);
    }

    public Task WaitForReady(CancellationToken cancellationToken = default) => _ready.Task.WaitAsync(cancellationToken);

    private async Task Drain(StreamReader reader, bool inspect)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), _readers.Token)) > 0)
            {
                if (!_ready.Task.IsCompletedSuccessfully && Interlocked.Add(ref _bytes, Encoding.UTF8.GetByteCount(buffer.AsSpan(0, count))) > 1048576)
                    throw new InvalidDataException("SSH forwarding diagnostic output exceeded its limit.");
                if (!inspect) continue;
                for (var index = 0; index < count; index++)
                {
                    if (buffer[index] == '\n') { Inspect(line.ToString().TrimEnd('\r')); line.Clear(); }
                    else if (line.Length < 32768) line.Append(buffer[index]);
                    else throw new InvalidDataException("SSH forwarding diagnostic line exceeded its limit.");
                }
            }
            if (inspect && line.Length > 0) Inspect(line.ToString());
        }
        catch (OperationCanceledException) when (_readers.IsCancellationRequested) { }
        catch
        {
            _readers.Cancel();
            Kill();
            throw;
        }
    }

    private void Inspect(string line)
    {
        lock (_gate)
        {
            if (line.StartsWith("Authenticated to ", StringComparison.Ordinal) || line.StartsWith("debug1: Authentication succeeded", StringComparison.Ordinal))
                _authenticated = true;
            if (_kind == ForwardingKind.Remote)
            {
                if (line.StartsWith("debug1: remote forward success for:", StringComparison.Ordinal) && line.Contains(":" + _port + ",", StringComparison.Ordinal)) _listener = true;
            }
            else
            {
                if (line.StartsWith("debug1: Local forwarding listening on ", StringComparison.Ordinal))
                    _listenerCandidate = line.Contains(" port " + _port + ".", StringComparison.Ordinal);
                if (line.StartsWith("bind ", StringComparison.Ordinal) || line.StartsWith("listen ", StringComparison.Ordinal)) _listenerCandidate = false;
                if (_listenerCandidate && line.StartsWith("debug1: channel ", StringComparison.Ordinal) && line.Contains(": new port-listener ", StringComparison.Ordinal)) _listener = true;
            }
            var failure = line.Contains("Address already in use", StringComparison.OrdinalIgnoreCase) ? "The forwarding listener port is already in use."
                : line.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ? "SSH authentication or forwarding permissions were denied."
                : line.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase) ? "SSH host-key verification failed."
                : line.Contains("remote port forwarding failed", StringComparison.OrdinalIgnoreCase) ? "The remote forwarding listener could not be established."
                : line.Contains("Broken pipe", StringComparison.OrdinalIgnoreCase) || line.Contains("Connection reset", StringComparison.OrdinalIgnoreCase)
                    ? "The SSH forwarding connection was interrupted." : null;
            if (failure is not null) { _failure = failure; _knownFailure = true; }
            if (_authenticated && _listener) _ready.TrySetResult();
        }
    }

    private async Task Observe(Task output, Task error)
    {
        try
        {
            await _process.WaitForExitAsync();
            ExitCode = _process.ExitCode;
            var drained = Task.WhenAll(output, error);
            try { await drained.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (TimeoutException)
            {
                _readers.Cancel();
                await drained;
            }
        }
        finally { _ready.TrySetException(new InvalidOperationException(_failure)); }
    }

    public async Task Stop()
    {
        _readers.Cancel();
        Kill();
        try { await Completion; }
        catch (IOException) { }
    }

    private void Kill()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    public async ValueTask DisposeAsync()
    {
        try { await Stop(); }
        finally { _process.Dispose(); _readers.Dispose(); }
    }
}
