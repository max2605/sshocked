using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;
using static Sshocked.Services.AzureCliSyntax;

namespace Sshocked.Services;

public sealed class AzureBastionService(IExternalProcessRunner runner, ILogger<AzureBastionService> logger) : IBastionService
{
    public async Task<BastionCliInfo> CheckPrerequisites(CancellationToken ct = default)
    {
        try
        {
            var versionResult = await runner.Run(AzureCliCommand.Create([VersionCommand, OutputOption, JsonOutput]), ct);
            AzureCliErrors.ThrowIfCertificateFailure(versionResult, "Read Azure CLI version");
            if (versionResult.ExitCode != 0) return new(false, null, false, false, false, "Install Azure CLI: https://aka.ms/azure-cli");
            using var versions = JsonDocument.Parse(versionResult.StandardOutput);
            Version.TryParse(versions.RootElement.GetProperty(CliVersionProperty).GetString(), out var version);
            var extensionsResult = await runner.Run(AzureCliCommand.Create([Extension, List, OutputOption, JsonOutput]), ct);
            AzureCliErrors.ThrowIfCertificateFailure(extensionsResult, "List Azure CLI extensions");
            var bastion = false;
            var ssh = false;
            if (extensionsResult.ExitCode == 0)
            {
                using var extensions = JsonDocument.Parse(extensionsResult.StandardOutput);
                foreach (var extension in extensions.RootElement.EnumerateArray())
                {
                    var name = extension.GetProperty(NameProperty).GetString();
                    bastion |= name == Bastion;
                    ssh |= name == Ssh;
                }
            }
            var account = await runner.Run(AzureCliCommand.Create([Account, Show, OutputOption, JsonOutput]), ct);
            AzureCliErrors.ThrowIfCertificateFailure(account, "Read Azure account");
            var error = version == null || version < new Version(2, 62, 0) ? "Upgrade Azure CLI to version 2.62.0 or later." :
                !bastion ? "Install Bastion support: az extension add --name bastion" :
                account.ExitCode != 0 ? "Sign in to Azure: az login" : null;
            return new(true, version, bastion, ssh, account.ExitCode == 0, error);
        }
        catch (Win32Exception) { return new(false, null, false, false, false, "Azure CLI not found. Install it from https://aka.ms/azure-cli and run az login."); }
        catch (JsonException) { return new(true, null, false, false, false, "Azure CLI returned invalid JSON. Upgrade Azure CLI and verify az version, az extension list and az account show."); }
    }

    public Task<int> RunNativeSsh(ServerHost host, string? command = null, CancellationToken ct = default, IReadOnlyDictionary<string, string?>? environment = null)
        => RunNativeSession(host, command, ct, environment, null);

    public async Task<int> RunNativeSession(ServerHost host, string? command, CancellationToken ct, IReadOnlyDictionary<string, string?>? environment, Func<Task>? onStarted)
    {
        var config = host.AzureBastion ?? throw new ArgumentException("Azure Bastion configuration is missing.");
        await EnsureReady(config, true, ct);
        ValidateNativeWindowsArguments(host, command);
        var info = AzureCliCommand.Create(BuildNativeArguments(host, command), captureOutput: false);
        if (environment != null) foreach (var variable in environment) info.Environment[variable.Key] = variable.Value;
        logger.LogInformation("Connecting to {Alias} through Azure Bastion native SSH", host.Alias);
        return (await runner.RunSession(info, onStarted, ct)).ExitCode;
    }

    public async Task<ProcessResult> RunNativeCommand(ServerHost host, string command, CancellationToken ct = default, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var config = host.AzureBastion ?? throw new ArgumentException("Azure Bastion configuration is missing.");
        await EnsureReady(config, true, ct);
        ValidateNativeWindowsArguments(host, command);
        var info = AzureCliCommand.Create(BuildNativeArguments(host, command, tty: false));
        if (environment != null) foreach (var variable in environment) info.Environment[variable.Key] = variable.Value;
        return await runner.Run(info, ct);
    }

    public async Task<IBastionTunnel> CreateTunnel(AzureBastionConfig config, CancellationToken ct = default)
    {
        Validate(config);
        if (config.AuthType == BastionAuthType.Aad)
            throw new InvalidOperationException("AAD authentication requires native Azure Bastion mode. Select SSH key/password authentication for tunnel mode.");
        await EnsureReady(config, false, ct);
        var port = config.LocalPort;
        var listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; }
        catch (SocketException ex) { throw new InvalidOperationException($"Bastion local port {port} is unavailable. Select another port or use 0 for automatic selection.", ex); }
        finally { listener.Stop(); }
        ct.ThrowIfCancellationRequested();
        var tunnel = new AzureBastionTunnel(runner.Start(AzureCliCommand.Create(BuildTunnelArguments(config, port))), port);
        try
        {
            await tunnel.WaitUntilReady(TimeSpan.FromSeconds(30), ct);
            logger.LogInformation("Azure Bastion tunnel ready on loopback port {Port}", port);
            return tunnel;
        }
        catch { await tunnel.DisposeAsync(); throw; }
    }

    public async Task<IReadOnlyList<BastionVmInfo>> ListTargetVms(string bastionName, string resourceGroup, CancellationToken ct = default)
    {
        var check = await runner.Run(AzureCliCommand.Create([Network, Bastion, Show, NameOption, bastionName, ResourceGroupOption, resourceGroup, OutputOption, JsonOutput]), ct);
        AzureCliErrors.ThrowIfCertificateFailure(check, "Inspect Azure Bastion");
        check.EnsureSuccess("Inspect Azure Bastion");
        var result = await runner.Run(AzureCliCommand.Create([Vm, List, ShowDetailsOption, OutputOption, JsonOutput]), ct);
        AzureCliErrors.ThrowIfCertificateFailure(result, "List candidate Azure VMs");
        result.EnsureSuccess("List candidate Azure VMs");
        using var document = JsonDocument.Parse(result.StandardOutput);
        var vms = new List<BastionVmInfo>();
        foreach (var vm in document.RootElement.EnumerateArray())
        {
            string? os = null;
            if (vm.TryGetProperty(StorageProfileProperty, out var storage) && storage.TryGetProperty(OsDiskProperty, out var disk) && disk.TryGetProperty(OsTypeProperty, out var osType)) os = osType.GetString();
            vms.Add(new(vm.GetProperty(NameProperty).GetString()!, vm.GetProperty(IdProperty).GetString()!, vm.TryGetProperty(PrivateIpsProperty, out var ips) ? ips.GetString() : null, os));
        }
        return vms;
    }

    private async Task EnsureReady(AzureBastionConfig config, bool needsSshExtension, CancellationToken ct)
    {
        Validate(config);
        var prerequisites = await CheckPrerequisites(ct);
        if (prerequisites.ErrorMessage != null) throw new InvalidOperationException(prerequisites.ErrorMessage);
        if (needsSshExtension && !prerequisites.HasSshExtension) throw new InvalidOperationException("Install native SSH support: az extension add --name ssh");
        if (!string.IsNullOrWhiteSpace(config.TenantId))
        {
            var args = new List<string> { Account, Show, QueryOption, TenantIdQuery, OutputOption, TsvOutput };
            if (!string.IsNullOrWhiteSpace(config.SubscriptionId)) args.AddRange([SubscriptionOption, config.SubscriptionId]);
            var account = await runner.Run(AzureCliCommand.Create(args), ct);
            AzureCliErrors.ThrowIfCertificateFailure(account, "Read Azure tenant");
            account.EnsureSuccess("Read Azure tenant");
            if (!string.Equals(account.StandardOutput.Trim(), config.TenantId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Sign in to the configured tenant: az login --tenant {config.TenantId}");
        }
    }

    public static void Validate(AzureBastionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BastionName) || string.IsNullOrWhiteSpace(config.BastionResourceGroup)) throw new ArgumentException("Bastion name and resource group are required.");
        var resource = !string.IsNullOrWhiteSpace(config.TargetResourceId);
        var ip = !string.IsNullOrWhiteSpace(config.TargetIpAddress);
        if (resource == ip) throw new ArgumentException("Specify exactly one target VM resource ID or IP address.");
        if (resource && !Regex.IsMatch(config.TargetResourceId!, TargetResourcePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Target VM ID must have the form /subscriptions/<id>/resourceGroups/<group>/providers/Microsoft.Compute/virtualMachines/<name>.");
        if (ip && !IPAddress.TryParse(config.TargetIpAddress, out _)) throw new ArgumentException("Target IP address is invalid.");
        if (config.TargetPort is < 1 or > 65535 || config.LocalPort is < 0 or > 65535) throw new ArgumentException("Bastion ports must be within 1–65535 (local port 0 selects an available port).");
    }

    public static IReadOnlyList<string> BuildNativeArguments(ServerHost host, string? command = null, bool tty = true)
    {
        var config = host.AzureBastion ?? throw new ArgumentException("Azure Bastion configuration is missing.");
        var args = BuildTargetArguments(config, Ssh);
        args.AddRange([AuthTypeOption, config.AuthType switch { BastionAuthType.Aad => AadAuthentication, BastionAuthType.Password => PasswordAuthentication, _ => KeyAuthentication }]);
        if (config.AuthType != BastionAuthType.Aad)
        {
            var user = config.Username ?? host.User;
            if (string.IsNullOrWhiteSpace(user)) throw new ArgumentException("Bastion SSH username is required.");
            args.AddRange([UsernameOption, user]);
        }
        if (config.AuthType == BastionAuthType.SshKey)
        {
            var key = config.SshKeyPath ?? host.IdentityFile;
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Bastion SSH key path is required.");
            args.AddRange([SshKeyOption, key]);
        }
        if (command != null) { args.Add("--"); if (tty) args.Add("-t"); args.Add(command); }
        return args;
    }

    private static void ValidateNativeWindowsArguments(ServerHost host, string? command)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Azure CLI's Bastion extension launches its inner SSH command with shell=True.
        // ArgumentList protects our az invocation, but cannot protect that second shell.
        if (command is not null)
            throw new NotSupportedException("Native Azure Bastion remote commands/hooks are unavailable on Windows because Azure CLI invokes a local shell. Use tunnel mode with SSH key/password authentication.");
        var config = host.AzureBastion!;
        foreach (var value in new[] { config.Username ?? host.User, config.SshKeyPath ?? host.IdentityFile })
            if (value is not null && value.IndexOfAny(['&', '|', '<', '>', '^', '%', '!', '"', '\r', '\n']) >= 0)
                throw new ArgumentException("Native Azure Bastion username/key path contains Windows shell syntax. Use tunnel mode for this connection.");
    }

    public static IReadOnlyList<string> BuildTunnelArguments(AzureBastionConfig config, int localPort)
    {
        var args = BuildTargetArguments(config, Tunnel);
        args.AddRange([PortOption, localPort.ToString(CultureInfo.InvariantCulture)]);
        return args;
    }

    private static List<string> BuildTargetArguments(AzureBastionConfig config, string verb)
    {
        Validate(config);
        var args = new List<string> { Network, Bastion, verb, NameOption, config.BastionName, ResourceGroupOption, config.BastionResourceGroup,
            ResourcePortOption, config.TargetPort.ToString(CultureInfo.InvariantCulture) };
        args.AddRange(!string.IsNullOrWhiteSpace(config.TargetResourceId) ? [TargetResourceIdOption, config.TargetResourceId!] : [TargetIpOption, config.TargetIpAddress!]);
        if (!string.IsNullOrWhiteSpace(config.SubscriptionId)) args.AddRange([SubscriptionOption, config.SubscriptionId]);
        return args;
    }
}
