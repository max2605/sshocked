using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SessionRecordingService(IConfigRepository configRepository, IExternalProcessRunner processRunner,
    SessionRepository repository, ILogger<SessionRecordingService> logger) : ISessionRecordingService
{
    public bool ShouldRecord(ServerHost host)
    {
        var settings = configRepository.Load().SessionRecording;
        if (settings.Mode == RecordingMode.Disabled || !settings.CaptureOutput || Matches(settings.ExcludeHosts, host)) return false;
        if (host.RecordSession is { } enabled) return enabled;
        if (settings.IncludeHosts.Count > 0) return Matches(settings.IncludeHosts, host);
        return settings.Enabled;
    }

    private static bool Matches(Dictionary<string, bool> hosts, ServerHost host) => hosts.Any(entry => entry.Value &&
        (entry.Key.Equals(host.Id, StringComparison.OrdinalIgnoreCase) || entry.Key.Equals(host.Alias, StringComparison.OrdinalIgnoreCase)));

    public async Task<int> Record(ProcessStartInfo startInfo, ServerHost host, CancellationToken cancellationToken = default,
        Func<Task>? onStarted = null)
    {
        var settings = configRepository.Load().SessionRecording;
        if (settings.CaptureInput) throw new NotSupportedException("Input recording requires a PTY backend and is not supported by the output-only recorder.");
        if (settings.Mode is not (RecordingMode.OutputOnly or RecordingMode.AsciinemaCast))
            throw new NotSupportedException("This recorder supports OutputOnly and AsciinemaCast. Full terminal and script formats require a PTY backend.");
        repository.ApplyRetention(settings);
        using var recording = repository.Create(host, settings.CaptureTiming, ConsoleDimension(true), ConsoleDimension(false));
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
        startInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
        int? exitCode = null;
        logger.LogInformation("Recording output-only session {SessionId} for {Alias}", recording.Metadata.Id, host.Alias);
        System.Console.Error.WriteLine($"Recording output only: {recording.Metadata.Id}. Input is not captured; terminal/TTY behavior may differ.");
        Process? process = null;
        Task? captured = null;
        Exception? operationFailure = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process = processRunner.Start(startInfo);
            var stdout = Pump(process.StandardOutput, System.Console.Out, recording);
            var stderr = Pump(process.StandardError, System.Console.Error, recording);
            captured = Task.WhenAll(stdout, stderr);
            if (onStarted is not null) await onStarted();
            var exited = process.WaitForExitAsync(cancellationToken);
            var readers = new List<Task> { stdout, stderr };
            while (!exited.IsCompleted && readers.Count > 0)
            {
                // WhenAll waits for both readers even when one has already faulted. Observe
                // each reader separately so a disk/console/read failure cannot strand SSH.
                var completed = await Task.WhenAny(readers.Append(exited));
                if (completed == exited) break;
                await completed;
                readers.Remove(completed);
            }
            await exited;
            await captured;
            exitCode = process.ExitCode;
            return exitCode.Value;
        }
        catch (Exception exception)
        {
            operationFailure = exception;
            if (process is not null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
            if (captured is not null)
            {
                try { await captured; }
                catch { /* Preserve the initiating failure after observing both output readers. */ }
            }
            throw;
        }
        finally
        {
            process?.Dispose();
            try
            {
                recording.Complete(exitCode, settings.Compress);
                repository.ApplyRetention(settings);
            }
            catch (Exception exception) when (operationFailure is not null)
            {
                logger.LogWarning("Could not finalize interrupted recording {SessionId}: {Error}", recording.Metadata.Id, exception.Message);
            }
        }
    }

    private static async Task Pump(StreamReader input, TextWriter output, SessionWriter recording)
    {
        var buffer = new char[SessionRecordingFiles.BufferSize];
        var pendingSurrogate = string.Empty;
        while (await input.ReadAsync(buffer.AsMemory()) is var count && count > 0)
        {
            var data = pendingSurrogate + new string(buffer, 0, count);
            pendingSurrogate = string.Empty;
            // A StreamReader chunk can split a UTF-16 surrogate pair even though its decoder
            // handles UTF-8 byte boundaries. Keep the pair together for JSON serialization.
            if (char.IsHighSurrogate(data[^1]))
            { pendingSurrogate = data[^1].ToString(); data = data[..^1]; }
            if (data.Length == 0) continue;
            recording.Append(data);
            await output.WriteAsync(data);
            await output.FlushAsync();
        }
        if (pendingSurrogate.Length > 0)
        {
            recording.Append("\uFFFD");
            await output.WriteAsync("\uFFFD");
            await output.FlushAsync();
        }
    }

    private static int ConsoleDimension(bool width)
    {
        try { return width ? System.Console.WindowWidth : System.Console.WindowHeight; }
        catch (IOException) { return width ? SessionRecordingFiles.DefaultWidth : SessionRecordingFiles.DefaultHeight; }
        catch (PlatformNotSupportedException) { return width ? SessionRecordingFiles.DefaultWidth : SessionRecordingFiles.DefaultHeight; }
    }
}
