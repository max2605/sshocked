using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ProcessService(ILogger<ProcessService> logger, IExternalProcessRunner runner, IBastionService bastionService, IAuditService? audit = null) : IProcessService
{
    public async Task RunSsh(ServerHost host)
    {
        var exitCode = await RunSshSession(host);
        new ProcessResult(exitCode, "", "").EnsureSuccess("SSH session");
    }

    public Task RunAzureBastionSsh(ServerHost host) => RunSsh(host);

    public async Task<int> RunSshSession(ServerHost host, string? initialCommand = null, CancellationToken ct = default)
    {
        var native = host.AzureBastion?.ConnectionMode == BastionConnectionMode.Native;
        await using var connection = await PrepareAsync(host, useTunnel: !native, ct);
        logger.LogInformation("Starting SSH session to {Alias}", host.Alias);
        int exit;
        if (native)
        {
            exit = await bastionService.RunNativeSshAsync(connection.Host, initialCommand, ct, connection.Environment);
        }
        else
        {
            var info = CreateSshStartInfo(connection, captureOutput: false, tty: initialCommand != null, initialCommand);
            exit = (await runner.RunAsync(info, ct)).ExitCode;
        }
        if (initialCommand != null) RecordCommand(host, initialCommand, exit);
        logger.LogInformation("SSH session to {Alias} exited with code {ExitCode}", host.Alias, exit);
        return exit;
    }

    public async Task<string> RunSshCommand(ServerHost host, string command)
    {
        var result = await RunSshCommandResult(host, command);
        result.EnsureSuccess("Remote SSH command");
        return result.StandardOutput + result.StandardError;
    }

    public async Task<ProcessResult> RunSshCommandResult(ServerHost host, string command, CancellationToken ct = default)
    {
        // AAD requires Azure CLI's short-lived SSH certificate; a TCP tunnel cannot
        // authenticate an AAD principal on its own.
        var aad = host.AzureBastion?.AuthType == BastionAuthType.Aad;
        await using var connection = await PrepareAsync(host, useTunnel: !aad, ct);
        var result = aad
            ? await bastionService.RunNativeCommandAsync(connection.Host, command, ct, connection.Environment)
            : await runner.RunAsync(CreateSshStartInfo(connection, captureOutput: true, tty: false, command), ct);
        RecordCommand(host, command, result.ExitCode);
        return result;
    }

    private void RecordCommand(ServerHost host, string command, int exitCode)
    {
        foreach (var password in new[] { host.SudoPassword, host.SshPassword })
            if (!string.IsNullOrEmpty(password)) command = command.Replace(password, "[REDACTED]", StringComparison.Ordinal);
        audit?.Record(new AuditEntry
        {
            EventType = "command.executed", Category = command.Contains("docker ", StringComparison.Ordinal) ? "docker" : "command", HostId = host.Id, Alias = host.Alias,
            Action = command, Success = exitCode == 0, ExitCode = exitCode
        });
    }

    public async Task RunSshInteractive(ServerHost host, string command)
    {
        var exitCode = await RunSshSession(host, command);
        new ProcessResult(exitCode, "", "").EnsureSuccess("Interactive remote SSH command");
    }

    public Task<ISshConnectionLease> PrepareConnectionAsync(ServerHost host, CancellationToken ct = default) => PrepareAsync(host, true, ct);

    private async Task<ISshConnectionLease> PrepareAsync(ServerHost host, bool useTunnel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (host.AuthType == AuthType.AzureBastion && host.AzureBastion == null) throw new ArgumentException("Azure Bastion configuration is missing.");
        var prepared = CopyConnectionHost(host);
        var lease = new SshConnectionLease(prepared);
        try
        {
            if (useTunnel && prepared.AzureBastion != null)
            {
                var config = prepared.AzureBastion;
                var tunnel = await bastionService.CreateTunnelAsync(config, ct);
                lease.Add(tunnel);
                await tunnel.WaitUntilReadyAsync(TimeSpan.FromSeconds(30), ct);
                prepared.HostName = "127.0.0.1";
                prepared.ProxyJump = null;
                prepared.Port = tunnel.LocalPort;
                prepared.User = config.Username ?? prepared.User;
                prepared.AuthType = config.AuthType == BastionAuthType.Password ? AuthType.Password : AuthType.SshKey;
                prepared.IdentityFile = config.SshKeyPath ?? prepared.IdentityFile;
                // Host-key verification must follow the VM, not an ephemeral port.
                prepared.SshHostKeyAlias = "bastion:" + (config.TargetResourceId ?? config.TargetIpAddress);
                prepared.AzureBastion = null;
            }
            return lease;
        }
        catch { await lease.DisposeAsync(); throw; }
    }

    private static ProcessStartInfo CreateSshStartInfo(ISshConnectionLease connection, bool captureOutput, bool tty, string? command)
    {
        var info = new ProcessStartInfo { FileName = "ssh", UseShellExecute = false, RedirectStandardOutput = captureOutput, RedirectStandardError = captureOutput, CreateNoWindow = captureOutput };
        foreach (var argument in SshArguments.Build(connection.Host, tty, command)) info.ArgumentList.Add(argument);
        foreach (var variable in connection.Environment) info.Environment[variable.Key] = variable.Value;
        return info;
    }

    private static ServerHost CopyConnectionHost(ServerHost host) => new()
    {
        Id = host.Id, Alias = host.Alias, HostName = host.HostName, Port = host.Port, User = host.User,
        AuthType = host.AuthType, IdentityFile = host.IdentityFile, CustomSshOptions = host.CustomSshOptions, SshHostKeyAlias = host.SshHostKeyAlias, ProxyJump = host.ProxyJump,
        AzureBastion = host.AzureBastion is not { } bastion ? null : new AzureBastionConfig
        {
            BastionName = bastion.BastionName, BastionResourceGroup = bastion.BastionResourceGroup, TargetResourceId = bastion.TargetResourceId, TargetIpAddress = bastion.TargetIpAddress,
            TargetPort = bastion.TargetPort, LocalPort = bastion.LocalPort, AuthType = bastion.AuthType, Username = bastion.Username, SshKeyPath = bastion.SshKeyPath,
            SubscriptionId = bastion.SubscriptionId, TenantId = bastion.TenantId, ConnectionMode = bastion.ConnectionMode
        }
    };

    private sealed class SshConnectionLease(ServerHost host) : ISshConnectionLease
    {
        private readonly List<IAsyncDisposable> resources = [];
        public ServerHost Host { get; } = host;
        public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, string?> Environment => Variables;
        public void Add(IAsyncDisposable resource) => resources.Add(resource);
        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            for (var i = resources.Count - 1; i >= 0; i--)
            {
                try { await resources[i].DisposeAsync(); }
                catch (Exception error) { failures.Add(error); }
            }
            resources.Clear(); Variables.Clear();
            if (failures.Count > 0) throw new AggregateException("SSH connection cleanup failed.", failures);
        }
    }
}
