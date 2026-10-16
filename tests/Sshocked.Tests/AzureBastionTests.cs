using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class AzureBastionTests
{
    private static ServerHost BastionHost() => new()
    {
        Alias = "private-vm",
        User = "user",
        IdentityFile = "C:/keys/my key.pem",
        AuthType = AuthType.AzureBastion,
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
        var runner = new AzureBastionTestsFakeRunner();
        runner.Results.Enqueue(new(0, "{\"azure-cli\":\"2.62.0\"}", ""));
        runner.Results.Enqueue(new(0, "[]", ""));
        runner.Results.Enqueue(new(1, "", "login required"));
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        var result = await service.CheckPrerequisites();
        Assert.True(result.IsInstalled);
        Assert.False(result.HasBastionExtension);
        Assert.False(result.IsLoggedIn);
        Assert.Contains("az extension add --name bastion", result.ErrorMessage);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task PrerequisiteCertificateFailuresPreserveTheAzureDiagnosis(int failureStage, bool failureToStandardOutput)
    {
        const string diagnostic = "ERROR: [SSL: certificate_verify_failed] unable to get local issuer certificate";
        var runner = new AzureBastionTestsFakeRunner();
        var successes = PrerequisiteResults();
        for (var stage = 0; stage < successes.Length; stage++)
            runner.Results.Enqueue(stage == failureStage
                ? new(1, failureToStandardOutput ? diagnostic : "", failureToStandardOutput ? "" : diagnostic)
                : successes[stage]);
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);

        var error = await Assert.ThrowsAsync<AzureCliCertificateException>(() => service.CheckPrerequisites());

        AssertCertificateFailure(error, 1, diagnostic);
        Assert.Equal(failureStage + 1, runner.Starts.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SuccessfulPrerequisitesIgnoreCertificateWarningText(int warningStage)
    {
        var runner = new AzureBastionTestsFakeRunner();
        var successes = PrerequisiteResults();
        for (var stage = 0; stage < successes.Length; stage++)
            runner.Results.Enqueue(stage == warningStage
                ? successes[stage] with { StandardError = "WARNING: previous request reported CERTIFICATE_VERIFY_FAILED" }
                : successes[stage]);
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);

        var result = await service.CheckPrerequisites();

        Assert.True(result.IsInstalled);
        Assert.True(result.HasBastionExtension);
        Assert.True(result.IsLoggedIn);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(3, runner.Starts.Count);
    }

    private static ProcessResult[] PrerequisiteResults() =>
    [
        new(0, "{\"azure-cli\":\"2.62.0\"}", ""),
        new(0, "[{\"name\":\"bastion\"},{\"name\":\"ssh\"}]", ""),
        new(0, "{}", "")
    ];

    [Fact]
    public async Task NativeConnectionPropagatesExitWithoutRedirectingInteractiveConsole()
    {
        var runner = new AzureBastionTestsFakeRunner();
        runner.Results.Enqueue(new(0, "{\"azure-cli\":\"2.62.0\"}", ""));
        runner.Results.Enqueue(new(0, "[{\"name\":\"bastion\"},{\"name\":\"ssh\"}]", ""));
        runner.Results.Enqueue(new(0, "{}", ""));
        runner.Results.Enqueue(new(17, "", ""));
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        Assert.Equal(17, await service.RunNativeSsh(BastionHost()));
        Assert.False(runner.Starts[^1].RedirectStandardOutput);
        Assert.False(runner.Starts[^1].RedirectStandardError);
    }

    [Theory]
    [InlineData(BastionAuthType.SshKey, false)]
    [InlineData(BastionAuthType.Password, false)]
    [InlineData(BastionAuthType.Aad, false)]
    [InlineData(BastionAuthType.SshKey, true)]
    [InlineData(BastionAuthType.Password, true)]
    [InlineData(BastionAuthType.Aad, true)]
    public async Task AllNativeAuthenticationTypesRequireTheSshExtension(BastionAuthType authType, bool command)
    {
        var runner = new AzureBastionTestsFakeRunner();
        runner.Results.Enqueue(new(0, "{\"azure-cli\":\"2.62.0\"}", ""));
        runner.Results.Enqueue(new(0, "[{\"name\":\"bastion\"}]", ""));
        runner.Results.Enqueue(new(0, "{}", ""));
        // A sentinel success prevents an unrelated empty-queue failure from passing this test.
        runner.Results.Enqueue(new(0, "", ""));
        var host = BastionHost();
        host.AzureBastion!.AuthType = authType;
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (command) await service.RunNativeCommand(host, "true");
            else await service.RunNativeSsh(host);
        });
        Assert.Contains("az extension add --name ssh", error.Message);
        Assert.DoesNotContain(runner.Starts, start => start.ArgumentList.Contains("bastion") && start.ArgumentList.Contains("network"));
    }

    [Theory]
    [InlineData("id", false)]
    [InlineData("true&ver", false)]
    [InlineData("id", true)]
    [InlineData("true&ver", true)]
    public async Task NativeWindowsCommandsCannotReachTheInnerLocalShell(string command, bool captureOutput)
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = ReadyNativeRunner();
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            if (captureOutput) await service.RunNativeCommand(BastionHost(), command);
            else await service.RunNativeSsh(BastionHost(), command);
        });
        Assert.Contains("tunnel mode", error.Message);
        Assert.Equal(3, runner.Starts.Count);
    }

    [Theory]
    [InlineData("user", "user&ver")]
    [InlineData("user", "user|ver")]
    [InlineData("username", "user%USERNAME%")]
    [InlineData("username", "user\"&ver")]
    [InlineData("identity", "C:/keys/key!name")]
    [InlineData("identity", "C:/keys/key^name")]
    [InlineData("key", "C:/keys/key<name")]
    [InlineData("key", "C:/keys/key>name")]
    [InlineData("key", "C:/keys/key\nname")]
    public async Task NativeWindowsInteractiveCredentialsRejectInnerShellSyntax(string field, string value)
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = ReadyNativeRunner();
        var host = BastionHost();
        switch (field)
        {
            case "user": host.User = value; break;
            case "username": host.AzureBastion!.Username = value; break;
            case "identity": host.IdentityFile = value; break;
            default: host.AzureBastion!.SshKeyPath = value; break;
        }
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.RunNativeSsh(host));
        Assert.Contains("Windows shell syntax", error.Message);
        Assert.Equal(3, runner.Starts.Count);
    }

    private static AzureBastionTestsFakeRunner ReadyNativeRunner()
    {
        var runner = new AzureBastionTestsFakeRunner();
        runner.Results.Enqueue(new(0, "{\"azure-cli\":\"2.62.0\"}", ""));
        runner.Results.Enqueue(new(0, "[{\"name\":\"bastion\"},{\"name\":\"ssh\"}]", ""));
        runner.Results.Enqueue(new(0, "{}", ""));
        runner.Results.Enqueue(new(0, "", ""));
        return runner;
    }

    [Fact]
    public void CustomOptionsPreserveWindowsPathsAndQuotedSpaces()
    {
        Assert.Equal(new[] { "-i", @"C:\Users\test\my key", "-o", "ProxyCommand=ssh -W %h:%p jump" },
            SshArguments.Tokenize("-i \"C:\\Users\\test\\my key\" -o 'ProxyCommand=ssh -W %h:%p jump'"));
        Assert.Throws<ArgumentException>(() => SshArguments.Tokenize("-i 'unterminated"));
        Assert.Equal(new[] { "-F", @"C:\ssh\config" }, SshArguments.Tokenize("-F \"C:\\\\ssh\\\\config\""));
        var info = SshArguments.Build(new ServerHost { User = "user", HostName = "example", IdentityFile = "key with spaces" }, command: "echo '$HOME' ; exit 4");
        Assert.Equal("echo '$HOME' ; exit 4", info[^1]);
        Assert.Equal("user@example", info[^2]);
        Assert.Equal("--", info[^3]);
        var withOptions = SshArguments.Build(new ServerHost { User = "user", HostName = "example", IdentityFile = "key", CustomSshOptions = "-o ServerAliveInterval=60" });
        Assert.Contains("ServerAliveInterval=60", withOptions);
        Assert.Contains("-i", withOptions);
        Assert.Throws<ArgumentException>(() => SshArguments.ValidateTunnelOptions(["-o", "ProxyCommand=nc elsewhere 22"]));
        Assert.Throws<ArgumentException>(() => SshArguments.ValidateTunnelOptions(["-p", "443"]));
        SshArguments.ValidateTunnelOptions(["-o", "ServerAliveInterval=60", "-v"]);
    }

    [Fact]
    public async Task TunnelReadinessAndDisposalManageTheProcessLifecycle()
    {
        var runner = new AzureBastionTestsTunnelRunner(fail: false);
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        var tunnel = await service.CreateTunnel(BastionHost().AzureBastion!);
        var pid = runner.StartedPid;
        Assert.True(tunnel.IsReady);
        Assert.InRange(tunnel.LocalPort, 1, 65535);
        await tunnel.DisposeAsync();
        AssertProcessExited(pid);
    }

    [Fact]
    public async Task TunnelStartupFailurePropagatesAzureExitAndCleansUp()
    {
        var runner = new AzureBastionTestsTunnelRunner(fail: true);
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        var error = await Assert.ThrowsAsync<ProcessExecutionException>(() => service.CreateTunnel(BastionHost().AzureBastion!));
        Assert.Equal(7, error.ExitCode);
        Assert.Contains("target not found", error.StandardError);
        AssertProcessExited(runner.StartedPid);
    }

    [Theory]
    [InlineData("ERROR: [SSL: certificate_verify_failed] unable to get local issuer certificate", false)]
    [InlineData("ERROR: [SSL: certificate_verify_failed] unable to get local issuer certificate", true)]
    [InlineData("ERROR: Certificate Verify Failed: unable to get local issuer certificate", false)]
    [InlineData("ERROR: Certificate Verify Failed: unable to get local issuer certificate", true)]
    [InlineData("ERROR: Certificate Verification Failed. Add the trusted proxy CA.", false)]
    [InlineData("ERROR: Certificate Verification Failed. Add the trusted proxy CA.", true)]
    public async Task TunnelCertificateFailuresExplainRemediationAndCleanUp(string diagnostic, bool failureToStandardOutput)
    {
        var runner = new AzureBastionTestsTunnelRunner(fail: true, failureOutput: diagnostic, failureToStandardOutput: failureToStandardOutput);
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);

        var error = await Assert.ThrowsAsync<AzureCliCertificateException>(() => service.CreateTunnel(BastionHost().AzureBastion!));

        AssertCertificateFailure(error, 7, diagnostic);
        AssertProcessExited(runner.StartedPid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TunnelCertificateFailuresRemainRecognizableAfterDiagnosticCaptureIsFull(bool failureToStandardOutput)
    {
        const string diagnostic = "ERROR: [SSL: CERTIFICATE_VERIFY_FAILED] unable to get local issuer certificate";
        var runner = new AzureBastionTestsTunnelRunner(fail: true, failureOutput: diagnostic, failureToStandardOutput: failureToStandardOutput, preludeLength: 20000);
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);

        var error = await Assert.ThrowsAsync<AzureCliCertificateException>(() => service.CreateTunnel(BastionHost().AzureBastion!));

        Assert.Equal(7, error.ExitCode);
        Assert.Contains("REQUESTS_CA_BUNDLE", error.Message);
        Assert.IsType<ProcessExecutionException>(error.InnerException);
        AssertProcessExited(runner.StartedPid);
    }

    [Fact]
    public async Task TunnelExitAfterReadinessPreservesTheCertificateDiagnosis()
    {
        const string diagnostic = "ERROR: [SSL: CERTIFICATE_VERIFY_FAILED] unable to get local issuer certificate";
        var runner = new AzureBastionTestsTunnelRunner(fail: true, failureOutput: diagnostic, failAfterReady: true);
        var service = new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance);
        await using var tunnel = await service.CreateTunnel(BastionHost().AzureBastion!);
        Assert.True(tunnel.IsReady);
        await runner.ReleaseFailure();

        var error = await Assert.ThrowsAsync<AzureCliCertificateException>(() => tunnel.WaitUntilReady(TimeSpan.FromSeconds(10)));

        AssertCertificateFailure(error, 7, diagnostic);
        Assert.False(tunnel.IsReady);
        AssertProcessExited(runner.StartedPid);
    }

    private static void AssertCertificateFailure(AzureCliCertificateException error, int exitCode, string diagnostic)
    {
        Assert.Equal(exitCode, error.ExitCode);
        Assert.Contains("Azure CLI", error.Message);
        Assert.Contains("REQUESTS_CA_BUNDLE", error.Message);
        Assert.Contains("PowerShell:", error.Message);
        Assert.Contains("Command Prompt:", error.Message);
        Assert.Contains("https://learn.microsoft.com/", error.Message);
        Assert.Contains(diagnostic, error.StandardError);
        var processError = Assert.IsType<ProcessExecutionException>(error.InnerException);
        Assert.Equal(exitCode, processError.ExitCode);
        Assert.Equal(error.StandardError, processError.StandardError);
    }

    private static void AssertProcessExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
        catch (ArgumentException) { }
    }
}
