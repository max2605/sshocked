using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SshRunnerService(IProcessService processService, IRecentConnectionService recents, IAuditService audit, ILogger<SshRunnerService> logger) : ISshRunnerService
{
    public async Task Connect(ServerHost host)
    {
        logger.LogInformation("Connecting to {Alias}", host.Alias);
        audit.Record(new() { Category = "connection", EventType = "connection.start", HostId = host.Id, Alias = host.Alias, Action = $"{host.User}@{host.HostName}:{host.Port}" });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var outcomeRecorded = false;
        try
        {
            var exitCode = await processService.RunSshSession(host);
            if (exitCode == 0) recents.Record(host);
            audit.Record(new() { Category = "connection", EventType = "connection.end", HostId = host.Id, Alias = host.Alias, Success = exitCode == 0, ExitCode = exitCode, DurationSeconds = timer.Elapsed.TotalSeconds });
            outcomeRecorded = true;
            new ProcessResult(exitCode, "", "").EnsureSuccess("SSH session");
        }
        catch (Exception ex)
        {
            if (!outcomeRecorded) audit.Record(new() { Category = "connection", EventType = "connection.end", HostId = host.Id, Alias = host.Alias, Success = false, ErrorMessage = ex.Message, DurationSeconds = timer.Elapsed.TotalSeconds });
            throw;
        }
    }
}
