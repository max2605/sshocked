using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MonitoringTransportTests
{
    private readonly MonitoringTransportTestDependencies dependencies = new();
    private MonitoringTransport Transport() => new(dependencies, dependencies, dependencies);
    private static ServerHost Host() => new() { Id = "host", HostName = "host.invalid", User = "operator" };

    [Fact]
    public async Task ProbeUsesOneLeaseBoundedCaptureAndNoninteractiveDefaults()
    {
        dependencies.PreparedHost.CustomSshOptions = "-o ServerAliveInterval=15";
        var result = await Transport().Execute(Host(), "printf 'probe'", new(4096, 10));
        Assert.Equal("probe-output", result.StandardOutput);
        Assert.Equal(1, dependencies.Prepares);
        Assert.True(dependencies.Lease!.IsDisposed);
        Assert.Equal(4096, dependencies.Limit);
        var info = Assert.IsType<System.Diagnostics.ProcessStartInfo>(dependencies.Info);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardInput);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.Contains("BatchMode=yes", info.ArgumentList);
        Assert.Contains("StrictHostKeyChecking=yes", info.ArgumentList);
        Assert.Contains("RequestTTY=no", info.ArgumentList);
        Assert.Contains("ClearAllForwardings=yes", info.ArgumentList);
        Assert.Contains("ForkAfterAuthentication=no", info.ArgumentList);
        Assert.Equal("printf 'probe'", info.ArgumentList.Last());
        Assert.Null(dependencies.Input);
    }

    [Fact]
    public async Task SudoCredentialUsesProtectedStdinAndNeverCommandArguments()
    {
        var host = Host();
        host.SudoPasswordSecretRef = new() { SecretId = "sudo", SecretType = SecretType.Password };
        await Transport().Execute(host, "smartctl -a -j -- '/dev/sda'", new(4096, 10, AllowSudo: true));
        Assert.Equal(1, dependencies.Resolves);
        Assert.Equal(dependencies.Secret.Value + "\n", dependencies.Input);
        Assert.DoesNotContain(dependencies.Secret.Value!, string.Join(' ', dependencies.Info!.ArgumentList));
        Assert.StartsWith("sudo -S -p '' -- sh -c ", dependencies.Info.ArgumentList.Last());
        Assert.True(dependencies.Lease!.IsDisposed);
    }

    [Fact]
    public async Task SudoWithoutCredentialFailsFastThroughNoninteractiveSudoInsteadOfPrompting()
    {
        await Transport().Execute(Host(), "smartctl -a -j -- '/dev/sda'", new(4096, 10, AllowSudo: true));
        Assert.StartsWith("sudo -n -- sh -c ", dependencies.Info!.ArgumentList.Last());
        Assert.Null(dependencies.Input);
        Assert.Equal(0, dependencies.Resolves);
    }

    [Fact]
    public async Task SudoIsNeverResolvedOrAddedWithoutExplicitPermission()
    {
        var host = Host();
        host.SudoPasswordSecretRef = new() { SecretId = "sudo", SecretType = SecretType.Password };
        await Transport().Execute(host, "uptime", new());
        Assert.Equal(0, dependencies.Resolves);
        Assert.Equal("uptime", dependencies.Info!.ArgumentList.Last());
        Assert.Null(dependencies.Input);
    }

    [Fact]
    public async Task InvalidSudoSecretTypeReleasesTheLeaseWithoutStartingTheProbe()
    {
        var host = Host();
        host.SudoPasswordSecretRef = new() { SecretId = "wrong", SecretType = SecretType.SshPrivateKey };
        await Assert.ThrowsAsync<ArgumentException>(() => Transport().Execute(host, "uptime", new(AllowSudo: true)));
        Assert.True(dependencies.Lease!.IsDisposed);
        Assert.Null(dependencies.Info);
        Assert.Equal(0, dependencies.Resolves);
    }

    [Fact]
    public async Task FailedCredentialResolutionDoesNotExposeTheProviderError()
    {
        dependencies.Secret = new() { Success = false, ErrorMessage = "token=provider-private-value" };
        var host = Host();
        host.SudoPasswordSecretRef = new() { SecretId = "sudo", SecretType = SecretType.Password };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Transport().Execute(host, "uptime", new(AllowSudo: true)));
        Assert.DoesNotContain("provider-private-value", error.Message);
        Assert.True(dependencies.Lease!.IsDisposed);
        Assert.Null(dependencies.Info);
    }

    [Fact]
    public async Task CallerCancellationReleasesTheLeaseAndRemainsCancellation()
    {
        dependencies.Work = async ct => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new(0, "", ""); };
        using var cancellation = new CancellationTokenSource();
        var operation = Transport().Execute(Host(), "uptime", new(), cancellation.Token);
        await dependencies.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(dependencies.Lease!.IsDisposed);
    }

    [Fact]
    public async Task ProbeDeadlineBecomesTimeoutAndStillReleasesTheLease()
    {
        dependencies.Work = async ct => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new(0, "", ""); };
        await Assert.ThrowsAsync<TimeoutException>(() => Transport().Execute(Host(), "uptime", new(TimeoutSeconds: 1)));
        Assert.True(dependencies.Lease!.IsDisposed);
    }

    [Fact]
    public async Task OutputFailureReleasesTheLease()
    {
        dependencies.Work = _ => throw new ProcessOutputLimitException(1024);
        await Assert.ThrowsAsync<ProcessOutputLimitException>(() => Transport().Execute(Host(), "uptime", new(MaxOutputBytes: 1024)));
        Assert.True(dependencies.Lease!.IsDisposed);
    }

    [Fact]
    public async Task NativeAadWithoutCheckedCredentialsIsRejectedBeforePreparation()
    {
        var host = Host();
        host.AzureBastion = new() { AuthType = BastionAuthType.Aad };
        await Assert.ThrowsAsync<NotSupportedException>(() => Transport().Execute(host, "uptime", new()));
        Assert.Equal(0, dependencies.Prepares);
        Assert.Null(dependencies.Info);
    }

    [Theory]
    [InlineData("-tt")]
    [InlineData("-f")]
    [InlineData("-L 13306:127.0.0.1:3306")]
    [InlineData("-o RequestTTY=force")]
    [InlineData("-o SessionType=none")]
    public async Task UnsafeCustomOptionsCannotOverrideTheNoninteractiveProbe(string options)
    {
        dependencies.PreparedHost.CustomSshOptions = options;
        await Assert.ThrowsAsync<ArgumentException>(() => Transport().Execute(Host(), "uptime", new()));
        Assert.Null(dependencies.Info);
        Assert.True(dependencies.Lease is null || dependencies.Lease.IsDisposed);
    }
}
