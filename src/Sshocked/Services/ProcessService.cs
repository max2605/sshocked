using System.Diagnostics;
using System.Text;
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

    public Task<ProcessResult> RunSshCommandResultNonInteractive(ServerHost host, string command, CancellationToken ct = default)
        => ExecuteCommand(host, command, input: null, sendInput: false, ct, nonInteractive: true);

    public Task<ProcessResult> RunSshCommandStreamingValidated(ServerHost host, string command,
        Action<ServerHost> validatePrepared, Action<string> output, bool nonInteractive = false, CancellationToken ct = default)
        => ExecuteCommand(host, command, input: null, sendInput: false, ct, output, nonInteractive, validatePrepared);

    public Task<ProcessResult> RunSshCommandStreaming(ServerHost host, string command, Action<string> output, CancellationToken ct = default)
        => ExecuteCommand(host, command, input: null, sendInput: false, ct, output);

    public Task<ProcessResult> RunSshCommandWithInput(ServerHost host, string command, string input, CancellationToken ct = default)
        => ExecuteCommand(host, command, input, sendInput: true, ct);

    public Task<ProcessResult> RunSshCommandWithInputNonInteractive(ServerHost host, string command, string input, CancellationToken ct = default)
        => ExecuteCommand(host, command, input, sendInput: true, ct, nonInteractive: true);

    private async Task<ProcessResult> ExecuteCommand(ServerHost host, string command, string? input, bool sendInput, CancellationToken ct,
        Action<string>? output = null, bool nonInteractive = false, Action<ServerHost>? validatePrepared = null)
    {
        var elapsed = Stopwatch.StartNew();
        var auditHost = host;
        try
        {
            // AAD needs the CLI-issued SSH certificate; a TCP tunnel cannot authenticate it.
            var requiresAad = host.AzureBastion?.AuthType == BastionAuthType.Aad && host.KeySecretRef is null && host.PasswordSecretRef is null;
            if (nonInteractive && requiresAad) throw new NotSupportedException("Batch commands require a checked key/password SSH tunnel.");
            var aad = !sendInput && requiresAad;
            await using var connection = await connectionPreparer.Prepare(host, useTunnel: !aad, ct);
            auditHost = connection.Host;
            validatePrepared?.Invoke(connection.Host);
            ProcessResult result;
            if (aad)
            {
                if (output is not null) throw new NotSupportedException("Live command output requires an SSH connection or Bastion tunnel with key/password authentication.");
                result = await bastionService.RunNativeCommand(connection.Host, command, ct, connection.Environment);
            }
            else
            {
                var info = CreateSshStartInfo(connection, captureOutput: true, tty: false, command, nonInteractive);
                result = nonInteractive ? await runner.RunBounded(info, 16777216, input, ct)
                    : sendInput ? await runner.RunWithInput(info, input!, ct)
                    : output is null ? await runner.Run(info, ct) : await runner.RunStreaming(info, output, ct);
                if (nonInteractive && output is not null)
                { output(result.StandardOutput); output(result.StandardError); }
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

    private static ProcessStartInfo CreateSshStartInfo(ISshConnectionLease connection, bool captureOutput, bool tty, string? command, bool nonInteractive = false)
    {
        var info = new ProcessStartInfo
        {
            FileName = SshConnectionConstants.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            CreateNoWindow = captureOutput,
            StandardOutputEncoding = captureOutput ? Encoding.UTF8 : null,
            StandardErrorEncoding = captureOutput ? Encoding.UTF8 : null
        };
        if (nonInteractive) NonInteractiveSshOptions.Apply(info, connection.Host);
        foreach (var argument in SshArguments.Build(connection.Host, tty, command)) info.ArgumentList.Add(argument);
        foreach (var variable in connection.Environment) info.Environment[variable.Key] = variable.Value;
        return info;
    }
}
