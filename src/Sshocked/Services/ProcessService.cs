using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ProcessService(ILogger<ProcessService> logger, IExternalProcessRunner runner, IBastionService bastionService, ISecretProviderRegistry secrets, IAuditService? audit = null, ISessionRecordingService? recorder = null, string? applicationExecutable = null, ILocalCredentialService? localCredentials = null) : IProcessService
{
    private readonly SshConnectionPreparer connectionPreparer = new(bastionService, secrets, applicationExecutable, localCredentials);

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
            exit = await bastionService.RunNativeSession(connection.Host, initialCommand, ct, connection.Environment, onStarted);
        }
        else
        {
            var info = CreateSshStartInfo(connection, captureOutput: false, tty: initialCommand != null, initialCommand);
            exit = recorder?.ShouldRecord(host) == true ? await recorder.Record(info, host, ct, onStarted) : (await runner.RunSession(info, onStarted, ct)).ExitCode;
        }
        if (initialCommand != null && auditCommand) RecordCommand(connection.Host, initialCommand, exit);
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
        var auditHost = host;
        try
        {
            // AAD needs the CLI-issued SSH certificate; a TCP tunnel cannot authenticate it.
            var aad = !sendInput && host.AzureBastion?.AuthType == BastionAuthType.Aad && host.KeySecretRef is null && host.PasswordSecretRef is null;
            await using var connection = await connectionPreparer.Prepare(host, useTunnel: !aad, ct);
            auditHost = connection.Host;
            ProcessResult result;
            if (aad) result = await bastionService.RunNativeCommand(connection.Host, command, ct, connection.Environment);
            else
            {
                var info = CreateSshStartInfo(connection, captureOutput: true, tty: false, command);
                result = sendInput ? await runner.RunWithInput(info, input!, ct) : await runner.Run(info, ct);
            }
            RecordCommand(auditHost, command, result.ExitCode, elapsed.Elapsed.TotalSeconds);
            return result;
        }
        catch (Exception ex)
        {
            RecordCommand(auditHost, command, null, elapsed.Elapsed.TotalSeconds, ex.GetType().Name);
            throw;
        }
    }

    private void RecordCommand(ServerHost host, string command, int? exitCode, double? durationSeconds = null, string? error = null)
    {
        command = SshCredentialRedactor.RedactCommand(command, host);
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

    public Task<ISshConnectionLease> PrepareConnection(ServerHost host, CancellationToken ct = default) => connectionPreparer.Prepare(host, true, ct);

    private static ProcessStartInfo CreateSshStartInfo(ISshConnectionLease connection, bool captureOutput, bool tty, string? command)
    {
        var info = new ProcessStartInfo { FileName = SshConnectionConstants.Executable, UseShellExecute = false, RedirectStandardOutput = captureOutput, RedirectStandardError = captureOutput, CreateNoWindow = captureOutput };
        foreach (var argument in SshArguments.Build(connection.Host, tty, command)) info.ArgumentList.Add(argument);
        foreach (var variable in connection.Environment) info.Environment[variable.Key] = variable.Value;
        return info;
    }
}
