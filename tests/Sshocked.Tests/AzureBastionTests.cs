using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class AzureBastionTests
{
    private static ServerHost BastionHost() => new()
    {
        Alias = "private-vm", User = "user", IdentityFile = "C:/keys/my key.pem", AuthType = AuthType.AzureBastion,
        AzureBastion = new AzureBastionConfig { BastionName = "bastion & resource", BastionResourceGroup = "rg", TargetResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm" }
    };

    [Fact]
    public void NativeArgumentsPreserveValuesAndNeverUseTenantAsUnsupportedCliOption()
    {
        var host = BastionHost();
        host.AzureBastion!.SubscriptionId = "subscription";
        host.AzureBastion.TenantId = "tenant";
        var args = AzureBastionService.BuildNativeArguments(host);
        Assert.Contains("bastion & resource", args);
        Assert.Contains("C:/keys/my key.pem", args);
        Assert.Contains("ssh-key", args);
        Assert.Contains("--subscription", args);
        Assert.DoesNotContain("--tenant", args);
        var info = AzureCliCommand.Create(args);
        Assert.Empty(info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.Contains("bastion & resource", info.ArgumentList);
    }

    [Fact]
    public void TunnelRequiresExactlyOneValidTargetAndValidPort()
    {
        var config = BastionHost().AzureBastion!;
        config.TargetIpAddress = "10.0.0.4";
        Assert.Throws<ArgumentException>(() => AzureBastionService.Validate(config));
        config.TargetResourceId = null;
        AzureBastionService.Validate(config);
        config.TargetIpAddress = "garbage";
        Assert.Throws<ArgumentException>(() => AzureBastionService.Validate(config));
        config.TargetIpAddress = "10.0.0.4";
        config.LocalPort = 65536;
        Assert.Throws<ArgumentException>(() => AzureBastionService.Validate(config));
    }

    [Fact]
    public async Task PrerequisitesReportMissingExtensionAndUnauthenticatedAccount()
    {
        var runner = new FakeRunner();
        runner.Results.Enqueue(new(0, "{\"azure-cli\":\"2.62.0\"}", ""));
        runner.Results.Enqueue(new(0, "[]", ""));
        runner.Results.Enqueue(new(1, "", "login required"));
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        var result = await service.CheckPrerequisitesAsync();
        Assert.True(result.IsInstalled);
        Assert.False(result.HasBastionExtension);
        Assert.False(result.IsLoggedIn);
        Assert.Contains("az extension add --name bastion", result.ErrorMessage);
    }

    [Fact]
    public async Task NativeConnectionPropagatesExitWithoutRedirectingInteractiveConsole()
    {
        var runner = new FakeRunner();
        runner.Results.Enqueue(new(0, "{\"azure-cli\":\"2.62.0\"}", ""));
        runner.Results.Enqueue(new(0, "[{\"name\":\"bastion\"}]", ""));
        runner.Results.Enqueue(new(0, "{}", ""));
        runner.Results.Enqueue(new(17, "", ""));
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        Assert.Equal(17, await service.RunNativeSshAsync(BastionHost()));
        Assert.False(runner.Starts[^1].RedirectStandardOutput);
        Assert.False(runner.Starts[^1].RedirectStandardError);
    }

    [Fact]
    public void CustomOptionsPreserveWindowsPathsAndQuotedSpaces()
    {
        Assert.Equal(new[] { "-i", @"C:\Users\test\my key", "-o", "ProxyCommand=ssh -W %h:%p jump" },
            SshArguments.Tokenize("-i \"C:\\Users\\test\\my key\" -o 'ProxyCommand=ssh -W %h:%p jump'"));
        Assert.Throws<ArgumentException>(() => SshArguments.Tokenize("-i 'unterminated"));
        var info = SshArguments.Build(new ServerHost { User = "user", HostName = "example", IdentityFile = "key with spaces" }, command: "echo '$HOME' ; exit 4");
        Assert.Equal("echo '$HOME' ; exit 4", info[^1]);
        Assert.Equal("user@example", info[^2]);
        Assert.Equal("--", info[^3]);
    }

    [Fact]
    public async Task TunnelReadinessAndDisposalManageTheProcessLifecycle()
    {
        var runner = new TunnelRunner(fail: false);
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        var tunnel = await service.CreateTunnelAsync(BastionHost().AzureBastion!);
        var pid = runner.StartedPid;
        Assert.True(tunnel.IsReady);
        Assert.InRange(tunnel.LocalPort, 1, 65535);
        await tunnel.DisposeAsync();
        AssertProcessExited(pid);
    }

    [Fact]
    public async Task TunnelStartupFailurePropagatesAzureExitAndCleansUp()
    {
        var runner = new TunnelRunner(fail: true);
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        var error = await Assert.ThrowsAsync<ProcessExecutionException>(() => service.CreateTunnelAsync(BastionHost().AzureBastion!));
        Assert.Equal(7, error.ExitCode);
        Assert.Contains("target not found", error.StandardError);
        AssertProcessExited(runner.StartedPid);
    }

    private static void AssertProcessExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
        catch (ArgumentException) { }
    }

    private sealed class TunnelRunner(bool fail) : IExternalProcessRunner
    {
        public int StartedPid { get; private set; }
        public Process Start(ProcessStartInfo ignored)
        {
            var info = new ProcessStartInfo { FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            if (OperatingSystem.IsWindows())
            {
                info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command");
                info.ArgumentList.Add(fail ? "[Console]::Error.WriteLine('target not found'); exit 7" : "Write-Output 'Tunnel is ready'; Start-Sleep -Seconds 60");
            }
            else { info.ArgumentList.Add("-c"); info.ArgumentList.Add(fail ? "echo 'target not found' >&2; exit 7" : "echo 'Tunnel is ready'; sleep 60"); }
            var process = Process.Start(info)!;
            StartedPid = process.Id;
            return process;
        }
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
        {
            var args = startInfo.ArgumentList;
            var result = args.Contains("version") ? new ProcessResult(0, "{\"azure-cli\":\"2.62.0\"}", "") :
                args.Contains("extension") ? new ProcessResult(0, "[{\"name\":\"bastion\"}]", "") : new ProcessResult(0, "{}", "");
            return Task.FromResult(result);
        }
    }

    private sealed class FakeRunner : IExternalProcessRunner
    {
        public Queue<ProcessResult> Results { get; } = new();
        public List<ProcessStartInfo> Starts { get; } = [];
        public Process Start(ProcessStartInfo startInfo) => throw new NotSupportedException();
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
        { Starts.Add(startInfo); return Task.FromResult(Results.Dequeue()); }
    }
}
