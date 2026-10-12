using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ProcessService(ILogger<ProcessService> logger, IExternalProcessRunner runner, IBastionService bastionService, ISecretProviderRegistry secrets, IAuditService? audit = null, ISessionRecordingService? recorder = null) : IProcessService
{
    private readonly SshConnectionPreparer connectionPreparer = new(bastionService, secrets);

    public async Task RunSsh(ServerHost host)
    {
        var exitCode = await RunSshSession(host);
        new ProcessResult(exitCode, "", "").EnsureSuccess("SSH session");
    }


    public async Task<int> RunSshSession(ServerHost host, string? initialCommand = null, CancellationToken ct = default, Func<Task>? onStarted = null, bool auditCommand = true)
    {
        var native = host.AzureBastion?.ConnectionMode == BastionConnectionMode.Native;
        await using var connection = await connectionPreparer.Prepare(host, useTunnel: !native, ct);
        logger.LogInformation("Starting SSH session to {Alias}", host.Alias);
        int exit;
        if (native)
        {
            if (recorder?.ShouldRecord(host) == true) throw new NotSupportedException("Recording native Azure Bastion sessions is unavailable. Select tunnel mode with SSH key/password authentication to record output.");
            exit = await bastionService.RunNativeSessionAsync(connection.Host, initialCommand, ct, connection.Environment, onStarted);
        }
        else
        {
            var info = CreateSshStartInfo(connection, captureOutput: false, tty: initialCommand != null, initialCommand);
            exit = recorder?.ShouldRecord(host) == true ? await recorder.Record(info, host, ct, onStarted) : (await runner.RunSessionAsync(info, onStarted, ct)).ExitCode;
        }
        if (initialCommand != null && auditCommand) RecordCommand(host, initialCommand, exit);
        logger.LogInformation("SSH session to {Alias} exited with code {ExitCode}", host.Alias, exit);
        return exit;
    }

    public async Task<string> RunSshCommand(ServerHost host, string command)
    {
        var result = await RunSshCommandResult(host, command);
        result.EnsureSuccess("Remote SSH command");
        return result.StandardOutput + result.StandardError;
    }

    public Task<ProcessResult> RunSshCommandResult(ServerHost host, string command, CancellationToken ct = default)
        => ExecuteCommand(host, command, input: null, sendInput: false, ct);

    public Task<ProcessResult> RunSshCommandWithInput(ServerHost host, string command, string input, CancellationToken ct = default)
        => ExecuteCommand(host, command, input, sendInput: true, ct);

    private async Task<ProcessResult> ExecuteCommand(ServerHost host, string command, string? input, bool sendInput, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        try
        {
            // AAD needs the CLI-issued SSH certificate; a TCP tunnel cannot authenticate it.
            var aad = !sendInput && host.AzureBastion?.AuthType == BastionAuthType.Aad && host.KeySecretRef is null && host.PasswordSecretRef is null;
            await using var connection = await connectionPreparer.Prepare(host, useTunnel: !aad, ct);
            ProcessResult result;
            if (aad) result = await bastionService.RunNativeCommandAsync(connection.Host, command, ct, connection.Environment);
            else
            {
                var info = CreateSshStartInfo(connection, captureOutput: true, tty: false, command);
                result = sendInput ? await runner.RunWithInputAsync(info, input!, ct) : await runner.RunAsync(info, ct);
            }
            RecordCommand(host, command, result.ExitCode, elapsed.Elapsed.TotalSeconds);
            return result;
        }
        catch (Exception ex)
        {
            RecordCommand(host, command, null, elapsed.Elapsed.TotalSeconds, ex.GetType().Name);
            throw;
        }
    }

    private void RecordCommand(ServerHost host, string command, int? exitCode, double? durationSeconds = null, string? error = null)
    {
        foreach (var password in new[] { host.SudoPassword, host.SshPassword })
            if (!string.IsNullOrEmpty(password)) command = command.Replace(password, SshConnectionConstants.Redacted, StringComparison.Ordinal);
        audit?.Record(new AuditEntry
        {
            EventType = SshConnectionConstants.CommandExecutedEvent,
            Category = command.Contains(SshConnectionConstants.DockerCommandPrefix, StringComparison.Ordinal) ? SshConnectionConstants.DockerAuditCategory : SshConnectionConstants.CommandAuditCategory,
            HostId = host.Id,
            Alias = host.Alias,
            Action = command,
            Success = exitCode == 0,
            ExitCode = exitCode,
            DurationSeconds = durationSeconds,
            ErrorMessage = error
        });
    }

    public async Task RunSshInteractive(ServerHost host, string command)
    {
        var exitCode = await RunSshSession(host, command);
        new ProcessResult(exitCode, "", "").EnsureSuccess("Interactive remote SSH command");
    }

    public Task<ISshConnectionLease> PrepareConnectionAsync(ServerHost host, CancellationToken ct = default) => connectionPreparer.Prepare(host, true, ct);

    private static ProcessStartInfo CreateSshStartInfo(ISshConnectionLease connection, bool captureOutput, bool tty, string? command)
    {
        var info = new ProcessStartInfo { FileName = SshConnectionConstants.Executable, UseShellExecute = false, RedirectStandardOutput = captureOutput, RedirectStandardError = captureOutput, CreateNoWindow = captureOutput };
        foreach (var argument in SshArguments.Build(connection.Host, tty, command)) info.ArgumentList.Add(argument);
        foreach (var variable in connection.Environment) info.Environment[variable.Key] = variable.Value;
        return info;
    }
}
