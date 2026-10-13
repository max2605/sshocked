using System.Diagnostics;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class RemoteCommandSession(Process process, ISshConnectionLease lease, ServerHost host, IAuditService audit,
    IConfigRepository repository, SessionRepository sessions, SessionWriter? recording, CancellationToken lifetimeToken) : IManagedCommandSession
{
    private readonly string prefix = "\x1eSSHOCKED_" + Guid.NewGuid().ToString("N") + ":";
    private readonly SemaphoreSlim gate = new(1);
    private readonly CancellationTokenSource readerCancellation = new();
    private Task stdout = Task.CompletedTask;
    private Task stderr = Task.CompletedTask;
    private Task exited = Task.CompletedTask;
    private CommandSessionPending? pending;
    private int sequence;
    private int disposed;

    public void ValidateRecordingCommand(string command)
    {
        MacroCommandPolicy.Validate(command, host);
        MacroCommandPolicy.Validate(command, lease.Host);
    }

    public void StartReaders()
    {
        exited = process.WaitForExitAsync();
        stdout = CommandSessionOutputReader.Read(process.StandardOutput, prefix, false, () => Volatile.Read(ref pending), readerCancellation.Token);
        stderr = CommandSessionOutputReader.Read(process.StandardError, prefix, true, () => Volatile.Read(ref pending), readerCancellation.Token);
    }

    public Task Initialize(CancellationToken cancellationToken) => ExecuteCore(":", _ => { }, false, cancellationToken);

    public Task<ProcessResult> Execute(string command, Action<string> output, CancellationToken cancellationToken = default)
        => ExecuteCore(command, output, true, cancellationToken);

    private async Task<ProcessResult> ExecuteCore(string command, Action<string> output, bool auditCommand, CancellationToken cancellationToken)
    {
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, cancellationToken);
        cancellationToken = operationCancellation.Token;
        if (string.IsNullOrWhiteSpace(command) || command.Contains('\0')) throw new ArgumentException("Enter a command without NUL characters.");
        await gate.WaitAsync(cancellationToken);
        var elapsed = Stopwatch.StartNew();
        var auditedCommand = SshCredentialRedactor.RedactCommand(SshCredentialRedactor.RedactCommand(command, host), lease.Host);
        int? exitCode = null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            var operation = new CommandSessionPending(++sequence, output, recording);
            Volatile.Write(ref pending, operation);
            // Commands cannot consume the shell's protocol input or SSH authentication.
            // Both streams carry a marker so all command stderr is drained before the next prompt.
            var script = "eval " + RemoteShell.Quote(command) + " </dev/null\n_sshocked_command_status=$?\n" +
                "command printf " + RemoteShell.Quote("\\036" + prefix[1..] + sequence + ":%s\\037") + " \"$_sshocked_command_status\" >&9\n" +
                "command printf " + RemoteShell.Quote("\\036" + prefix[1..] + sequence + ":%s\\037") + " \"$_sshocked_command_status\" >&8\n";
            await process.StandardInput.WriteAsync(script.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            // A shell descendant can keep stdout/stderr open after the shell exits.
            // Detect the session process exit directly instead of waiting for pipe EOF.
            var completed = await Task.WhenAny(operation.Completion, stdout, stderr, exited).WaitAsync(cancellationToken);
            if (!operation.Completion.IsCompleted)
            {
                await completed;
                throw new IOException("The remote command shell exited before completing the command. Commands that exit or replace the shell end the session.");
            }
            await operation.Completion;
            var result = operation.Result();
            exitCode = result.ExitCode;
            return result;
        }
        catch
        {
            try { await DisposeAsync(); }
            catch { /* Cleanup must not replace the initiating command or cancellation failure. */ }
            throw;
        }
        finally
        {
            Volatile.Write(ref pending, null);
            if (auditCommand) audit.Record(new()
            {
                Category = "command",
                EventType = "command.session",
                HostId = host.Id,
                Alias = host.Alias,
                Action = auditedCommand,
                Success = exitCode == 0,
                ExitCode = exitCode,
                DurationSeconds = elapsed.Elapsed.TotalSeconds
            });
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            try
            {
                await process.StandardInput.WriteLineAsync("exit");
                await process.StandardInput.FlushAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(300));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException) { }
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromMilliseconds(300)); }
            catch (TimeoutException)
            {
                // A local ProxyCommand or shell descendant can inherit SSH pipe handles after
                // its parent exits. Reader cancellation keeps session cleanup bounded.
                readerCancellation.Cancel();
                process.StandardOutput.Dispose(); process.StandardError.Dispose();
                try { await Task.WhenAll(stdout, stderr); }
                catch { }
            }
            catch { /* Observe readers after process shutdown without concealing the original failure. */ }
            if (recording is not null)
            {
                var settings = repository.Load().SessionRecording;
                recording.Complete(process.ExitCode, settings.Compress);
                sessions.ApplyRetention(settings);
            }
            audit.Record(new()
            {
                Category = "connection",
                EventType = "connection.command-session.end",
                HostId = host.Id,
                Alias = host.Alias,
                Action = "Managed command session",
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode
            });
        }
        finally { readerCancellation.Dispose(); recording?.Dispose(); process.Dispose(); await lease.DisposeAsync(); }
    }
}
