using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class HookExecutorService(IExternalProcessRunner processRunner, IAuditService auditService,
    ILogger<HookExecutorService> logger) : IHookExecutorService, IAsyncDisposable
{
    private readonly object backgroundGate = new();
    private readonly HashSet<Task> backgroundTasks = [];
    private readonly CancellationTokenSource shutdown = new();
    private int disposalStarted;
    private readonly HookRemoteSessionCommand remoteSession = new(auditService, logger);

    public async Task<bool> RunLocalHooks(HookPhase phase, ServerHost host, GlobalHooks? globalHooks,
        HookContext context, CancellationToken cancellationToken = default)
    {
        foreach (var hook in HookDefinition.GetOrderedHooks(phase, host, globalHooks))
        {
            cancellationToken.ThrowIfCancellationRequested();
            HookDefinition.Validate(hook, phase);
            if (hook.Mode == HookExecutionMode.RemoteCommand) continue;
            if (!hook.Blocking)
            {
                var task = ExecuteAndHandle(hook, phase, host, context, cancellationToken);
                lock (backgroundGate) backgroundTasks.Add(task);
                _ = ObserveBackground(task);
                continue;
            }
            if (!await ExecuteAndHandle(hook, phase, host, context, cancellationToken) && !hook.ContinueOnError)
                return false;
        }
        return true;
    }

    private async Task ObserveBackground(Task task)
    {
        try { await task; }
        catch (Exception exception) { logger.LogWarning("Background hook cleanup failed: {Error}", exception.Message); }
        finally { lock (backgroundGate) backgroundTasks.Remove(task); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposalStarted, 1) != 0) return;
        await shutdown.CancelAsync();
        Task[] pending;
        lock (backgroundGate) pending = backgroundTasks.ToArray();
        try { await Task.WhenAll(pending); }
        catch (Exception exception) { logger.LogWarning("Background hook shutdown failed: {Error}", exception.Message); }
        shutdown.Dispose();
    }

    private async Task<bool> ExecuteAndHandle(HookScript hook, HookPhase phase, ServerHost host,
        HookContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string? temporaryScript = null;
        int? exitCode = null;
        var success = false;
        try
        {
            var environment = HookDefinition.BuildEnvironment(host, phase, context, hook);
            var startInfo = HookLocalProcess.BuildLocalStartInfo(hook, environment, out temporaryScript);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(hook.TimeoutSeconds));
            var result = await processRunner.Run(startInfo, timeout.Token);
            exitCode = result.ExitCode;
            success = exitCode == 0;
            PublishOutput(hook.OutputMode, result.StandardOutput, result.StandardError);
            if (!success)
                logger.LogWarning("{Phase} hook for {Alias} exited with code {ExitCode}", HookDefinition.PhaseName(phase), host.Alias, exitCode);
            else logger.LogInformation("{Phase} hook for {Alias} completed", HookDefinition.PhaseName(phase), host.Alias);
            return success;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !shutdown.IsCancellationRequested)
        {
            logger.LogWarning("{Phase} hook for {Alias} timed out after {Timeout} seconds", HookDefinition.PhaseName(phase), host.Alias, hook.TimeoutSeconds);
            return false;
        }
        catch (OperationCanceledException)
        {
            if (hook.Blocking) throw;
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning("{Phase} hook for {Alias} failed: {Error}", HookDefinition.PhaseName(phase), host.Alias, exception.Message);
            return false;
        }
        finally
        {
            if (temporaryScript is not null)
            {
                try { File.Delete(temporaryScript); }
                catch (IOException exception) { logger.LogWarning("Could not remove temporary hook script: {Error}", exception.Message); }
                catch (UnauthorizedAccessException exception) { logger.LogWarning("Could not remove temporary hook script: {Error}", exception.Message); }
            }
            auditService.Record(new AuditEntry
            {
                EventType = HookConstants.AuditCategory,
                Category = HookConstants.AuditCategory,
                HostId = host.Id,
                Alias = host.Alias,
                Action = $"{HookDefinition.PhaseName(phase)} ({hook.Mode})",
                Success = success,
                ExitCode = exitCode,
                DurationSeconds = stopwatch.Elapsed.TotalSeconds
            });
        }
    }

    private void PublishOutput(HookOutputMode mode, string output, string error)
    {
        if (mode is HookOutputMode.Log or HookOutputMode.Both)
        {
            if (!string.IsNullOrEmpty(output)) logger.LogInformation("Hook output: {Output}", output.TrimEnd());
            if (!string.IsNullOrEmpty(error)) logger.LogWarning("Hook error output: {Output}", error.TrimEnd());
        }
        if (mode is HookOutputMode.Console or HookOutputMode.Both)
        {
            if (!string.IsNullOrEmpty(output)) System.Console.Write(output);
            if (!string.IsNullOrEmpty(error)) System.Console.Error.Write(error);
        }
    }

    public string? BuildRemoteSessionCommand(ServerHost host, GlobalHooks? globalHooks, HookContext context)
        => remoteSession.Build(host, globalHooks, context);
}
