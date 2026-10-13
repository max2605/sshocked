using System.Diagnostics;
using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MonitoringTransport(IProcessService connections, IExternalProcessRunner runner,
    ISecretProviderRegistry secrets) : IMonitoringTransport
{
    public async Task<ProcessResult> Execute(ServerHost host, string command, MonitoringCommandOptions options,
        CancellationToken cancellationToken = default)
    {
        if (host.AzureBastion?.AuthType == BastionAuthType.Aad && host.KeySecretRef is null && host.PasswordSecretRef is null)
            throw new NotSupportedException("Monitoring needs checked SSH results; use a key/password Bastion tunnel.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            await using var lease = await connections.PrepareConnection(host, timeout.Token);
            string? input = null;
            if (options.AllowSudo)
            {
                var password = host.SudoPassword;
                if (host.SudoPasswordSecretRef is { } reference)
                {
                    if (reference.SecretType != SecretType.Password) throw new ArgumentException("Sudo requires a password secret.");
                    var resolved = await secrets.Resolve(reference, timeout.Token);
                    if (!resolved.Success || resolved.Value is null) throw new InvalidOperationException("Could not resolve sudo credentials.");
                    password = resolved.Value;
                }
                command = string.IsNullOrEmpty(password)
                    ? "sudo -n -- sh -c " + RemoteShell.Quote(command)
                    : "sudo -S -p '' -- sh -c " + RemoteShell.Quote(command);
                if (!string.IsNullOrEmpty(password)) input = password + "\n";
            }
            var info = new ProcessStartInfo("ssh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            NonInteractiveSshOptions.Apply(info, lease.Host);
            foreach (var argument in SshArguments.Build(lease.Host, command: command)) info.ArgumentList.Add(argument);
            foreach (var variable in lease.Environment) info.Environment[variable.Key] = variable.Value;
            return await runner.RunBounded(info, options.MaxOutputBytes, input, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Monitoring SSH probe timed out."); }
    }
}
