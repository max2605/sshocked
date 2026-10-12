using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SshRunnerService(IProcessService processService, IRecentConnectionService recents, IAuditService audit, IHookExecutorService hooks, IConfigRepository repository, ILogger<SshRunnerService> logger) : ISshRunnerService
{
    public async Task Connect(ServerHost host)
    {
        logger.LogInformation("Connecting to {Alias}", host.Alias);
        audit.Record(new() { Category = "connection", EventType = "connection.start", HostId = host.Id, Alias = host.Alias, Action = $"{host.User}@{host.HostName}:{host.Port}" });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var config = repository.Load();
        var context = new HookContext { ConfigDirectory = ConfigPaths.DirectoryPath, GroupName = config.Groups.FirstOrDefault(g => g.Id == host.GroupId)?.Name ?? "" };
        int? exitCode = null;
        Exception? failure = null;
        var started = false;
        try
        {
            if (!await hooks.RunLocalHooks(HookPhase.PreConnect, host, config.GlobalHooks, context))
                throw new InvalidOperationException("A pre-connect hook stopped the connection.");
            var command = hooks.BuildRemoteSessionCommand(host, config.GlobalHooks, context);
            exitCode = await processService.RunSshSession(host, command, onStarted: async () =>
            {
                started = true;
                if (!await hooks.RunLocalHooks(HookPhase.PostConnect, host, config.GlobalHooks, context))
                    throw new InvalidOperationException("A post-connect hook stopped the connection.");
            }, auditCommand: false);
            new ProcessResult(exitCode.Value, "", "").EnsureSuccess("SSH session");
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            var cleanupContext = context with { ExitCode = exitCode, DurationSeconds = timer.Elapsed.TotalSeconds };
            Exception? cleanupFailure = null;
            try
            {
                if (started && !await hooks.RunLocalHooks(HookPhase.PreDisconnect, host, config.GlobalHooks, cleanupContext))
                    cleanupFailure = new InvalidOperationException("A pre-disconnect hook failed.");
            }
            catch (Exception ex) { cleanupFailure = ex; }
            try
            {
                if (!await hooks.RunLocalHooks(HookPhase.PostDisconnect, host, config.GlobalHooks, cleanupContext))
                    cleanupFailure ??= new InvalidOperationException("A post-disconnect hook failed.");
            }
            catch (Exception ex) { cleanupFailure ??= ex; }
            if (cleanupFailure is not null) logger.LogWarning(cleanupFailure, "Connection cleanup hook failed for {Alias}", host.Alias);
            var success = failure is null && cleanupFailure is null && exitCode == 0;
            if (success) recents.Record(host);
            audit.Record(new()
            {
                Category = "connection",
                EventType = "connection.end",
                HostId = host.Id,
                Alias = host.Alias,
                Success = success,
                ExitCode = exitCode,
                ErrorMessage = (failure ?? cleanupFailure)?.GetType().Name,
                DurationSeconds = timer.Elapsed.TotalSeconds
            });
            if (failure is null && cleanupFailure is not null) throw cleanupFailure;
        }
    }
}
