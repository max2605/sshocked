using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class DockerSecretTests
{
    internal const string Password = "super-private-sudo-password";

    [Fact]
    public async Task PasswordSecretHostIsScannedThroughNoninteractiveSsh()
    {
        var host = new ServerHost
        {
            Id = "host",
            Alias = "vm",
            User = "user",
            HostName = "vm",
            AuthType = AuthType.Password,
            PasswordSecretRef = new() { SecretId = "login-password", SecretType = SecretType.Password }
        };
        var repository = new DockerSecretTestsRepository(host);
        var process = new DockerSecretTestsScanProcess();
        var service = new DockerService(process, repository, new DockerSecretTestsSecrets(), NullLogger<DockerService>.Instance);
        var result = await service.Scan(host);
        Assert.True(result.DockerAvailable);
        Assert.Equal("27.0.0", result.ServerVersion);
        Assert.Equal(3, process.Commands.Count);
        Assert.Empty(process.Inputs);
        Assert.NotNull(host.DockerInfo);
    }

    [Fact]
    public async Task MemorySshPasswordHostIsNotSkippedDuringScan()
    {
        var host = new ServerHost { Id = "host", Alias = "vm", User = "user", HostName = "vm", AuthType = AuthType.Password, SshPassword = Password };
        var process = new DockerSecretTestsScanProcess();
        var result = await new DockerService(process, new DockerSecretTestsRepository(host), new DockerSecretTestsSecrets(), NullLogger<DockerService>.Instance).Scan(host);
        Assert.True(result.DockerAvailable);
        Assert.Equal(3, process.Commands.Count);
        Assert.All(process.Commands, command => Assert.DoesNotContain(Password, command));
    }

    [Fact]
    public async Task DockerCachePreservesConcurrentHostIdentityAndSecretEdits()
    {
        var host = new ServerHost { Id = "host", Alias = "vm", User = "user", HostName = "vm", IdentityFile = "old-key" };
        var repository = new DockerSecretTestsConcurrentRepository(host);
        var result = await new DockerService(new DockerSecretTestsScanProcess(), repository, new DockerSecretTestsSecrets(), NullLogger<DockerService>.Instance).Scan(host);
        Assert.True(result.DockerAvailable);
        Assert.Equal("rotated-key", repository.Config.Hosts[0].IdentityFile);
        Assert.Equal("rotated-secret", repository.Config.Hosts[0].KeySecretRef?.SecretId);
        Assert.Same(result, repository.Config.Hosts[0].DockerInfo);
        Assert.Same(result, host.DockerInfo);
    }

    [Fact]
    public async Task InteractiveDockerCommandsQuoteContainerNamesAndLeaveSudoToTerminal()
    {
        var host = new ServerHost { SudoPassword = Password, SudoPasswordSecretRef = new() { SecretId = "sudo", SecretType = SecretType.Password } };
        var process = new DockerSecretTestsScanProcess();
        var secrets = new DockerSecretTestsSecrets();
        var service = new DockerService(process, new DockerSecretTestsRepository(host), secrets, NullLogger<DockerService>.Instance);
        var container = new ContainerModel { Name = "name'; touch sentinel; #" };
        await service.Exec(host, container);
        await service.Logs(host, container);
        Assert.Equal(new[] { "sudo docker exec -it -- 'name'\\''; touch sentinel; #' sh", "sudo docker logs -f -- 'name'\\''; touch sentinel; #'" }, process.Commands);
        Assert.All(process.Commands, command => Assert.DoesNotContain(Password, command));
        Assert.Empty(process.Inputs);
        Assert.Equal(0, secrets.ResolveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GroupDockerCommandsUseStdinForMemoryOrSecretSudoPassword(bool secretReference)
    {
        var container = new ContainerModel { Id = "container", Name = "name'; touch sentinel; #" };
        var host = new ServerHost { Id = "host", Alias = "vm", User = "user", HostName = "vm", SavedContainers = [container] };
        if (secretReference) host.SudoPasswordSecretRef = new() { SecretId = "sudo", SecretType = SecretType.Password };
        else host.SudoPassword = Password;
        var config = new AppConfig { Hosts = [host] };
        var process = new DockerSecretTestsScanProcess();
        var service = new ContainerGroupService(process, new DockerSecretTestsTerminals(), new DockerSecretTestsConsoleHelper(), new DockerSecretTestsSecrets(), NullLogger<ContainerGroupService>.Instance);
        await service.RunCommandOnAll(config, [container], "id");
        Assert.Equal(Password + "\n", Assert.Single(process.Inputs));
        Assert.Equal("sudo -S -p '' docker exec -- 'name'\\''; touch sentinel; #' sh -lc 'id'", Assert.Single(process.Commands));
        Assert.DoesNotContain(Password, process.Commands[0]);
    }

    [Fact]
    public async Task SudoSecretIsSentOnlyAsStdinAndNeverStoredOrLogged()
    {
        var host = new ServerHost
        {
            Id = "host",
            Alias = "vm",
            User = "user",
            HostName = "vm",
            SudoPasswordSecretRef = new() { SecretId = "sudo-password", SecretType = SecretType.Password }
        };
        var repository = new DockerSecretTestsRepository(host);
        var process = new DockerSecretTestsScanProcess();
        var secrets = new DockerSecretTestsSecrets();
        var log = new DockerSecretTestsCaptureLogger<DockerService>();
        var result = await new DockerService(process, repository, secrets, log).Scan(host);
        Assert.True(result.DockerAvailable);
        Assert.Equal(3, process.Inputs.Count);
        Assert.All(process.Inputs, input => Assert.Equal(Password + "\n", input));
        Assert.All(process.Commands, command => { Assert.Contains("sudo -S -p ''", command); Assert.DoesNotContain(Password, command); });
        Assert.DoesNotContain(Password, string.Join('\n', log.Lines));
        Assert.Null(host.SudoPassword);
        Assert.DoesNotContain(Password, JsonSerializer.Serialize(repository.Config, AppConfigContext.Default.AppConfig));
    }

    [Fact]
    public async Task MemorySudoPasswordWithQuoteUsesProtectedInputAndDoesNotLeakIntoAudit()
    {
        const string password = "mem'ory-sudo-private";
        var host = new ServerHost { Id = "host", Alias = "vm", User = "user", HostName = "vm", SudoPassword = password };
        var repository = new DockerSecretTestsRepository(host);
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-memory-sudo-tests-" + Guid.NewGuid().ToString("N"));
        var auditPath = Path.Combine(directory, "audit.jsonl");
        var runner = new DockerSecretTestsAuditScanRunner();
        var processLog = new DockerSecretTestsCaptureLogger<ProcessService>();
        var dockerLog = new DockerSecretTestsCaptureLogger<DockerService>();
        try
        {
            using var audit = new AuditService(repository, NullLogger<AuditService>.Instance, auditPath);
            var process = new ProcessService(processLog, runner, new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance), new DockerSecretTestsSecrets(), audit);
            var result = await new DockerService(process, repository, new DockerSecretTestsSecrets(), dockerLog).Scan(host);
            Assert.True(result.DockerAvailable);
            Assert.Equal(3, runner.Inputs.Count);
            Assert.All(runner.Inputs, input => Assert.Equal(password + "\n", input));
            Assert.Equal(3, runner.OutputLimits.Count);
            Assert.All(runner.OutputLimits, limit => Assert.Equal(16_777_216, limit));
            Assert.All(runner.StartInfos, info =>
            {
                Assert.Contains("StrictHostKeyChecking=yes", info.ArgumentList);
                Assert.Contains("RequestTTY=no", info.ArgumentList);
                Assert.Contains("ClearAllForwardings=yes", info.ArgumentList);
                Assert.Contains("PermitLocalCommand=no", info.ArgumentList);
                Assert.Contains("BatchMode=yes", info.ArgumentList);
                Assert.DoesNotContain(password, string.Join(' ', info.ArgumentList));
            });
            Assert.All(runner.Commands, command =>
            {
                Assert.Contains("sudo -S -p ''", command);
                Assert.DoesNotContain("| sudo", command);
                Assert.DoesNotContain("ory-sudo-private", command);
            });
            Assert.Equal(3, audit.Query(new(Type: "docker")).Count);
            Assert.DoesNotContain("ory-sudo-private", File.ReadAllText(auditPath));
            Assert.DoesNotContain("ory-sudo-private", string.Join('\n', processLog.Lines.Concat(dockerLog.Lines)));
            Assert.DoesNotContain("ory-sudo-private", JsonSerializer.Serialize(repository.Config, AppConfigContext.Default.AppConfig));
            Assert.Equal(password, host.SudoPassword);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedCommandKeepsCredentialOutsideSshArguments(bool nonInteractive)
    {
        var runner = new DockerSecretTestsInputRunner();
        var logger = new DockerSecretTestsCaptureLogger<ProcessService>();
        var service = new ProcessService(logger, runner, new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance), new DockerSecretTestsSecrets());
        var host = new ServerHost { User = "user", HostName = "vm" };
        var result = nonInteractive
            ? await service.RunSshCommandWithInputNonInteractive(host, "sudo -S -p '' docker info", Password + "\n")
            : await service.RunSshCommandWithInput(host, "sudo -S -p '' docker info", Password + "\n");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Password + "\n", runner.Input);
        Assert.DoesNotContain(Password, string.Join(' ', runner.Info!.ArgumentList));
        Assert.DoesNotContain(Password, string.Join('\n', logger.Lines));
        Assert.DoesNotContain(Password, runner.Info.Arguments);
        if (nonInteractive)
        {
            Assert.Equal(16_777_216, runner.OutputLimit);
            Assert.Contains("StrictHostKeyChecking=yes", runner.Info.ArgumentList);
            Assert.Contains("RequestTTY=no", runner.Info.ArgumentList);
            Assert.Contains("ClearAllForwardings=yes", runner.Info.ArgumentList);
        }
        else Assert.Null(runner.OutputLimit);
    }

    [Theory]
    [InlineData("-oLocalCommand=unsafe")]
    [InlineData("-oRequestTTY=force")]
    [InlineData("-R 9000:localhost:80")]
    public async Task NoninteractiveProtectedCommandRejectsUnsafeCustomOptionsBeforeStarting(string customOptions)
    {
        var runner = new DockerSecretTestsInputRunner();
        var service = new ProcessService(NullLogger<ProcessService>.Instance, runner,
            new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance), new DockerSecretTestsSecrets());
        await Assert.ThrowsAsync<ArgumentException>(() => service.RunSshCommandWithInputNonInteractive(
            new() { HostName = "vm", User = "user", CustomSshOptions = customOptions }, "sudo -S -p '' docker info", Password + "\n"));
        Assert.Null(runner.Info);
        Assert.Null(runner.Input);
    }

    [Fact]
    public async Task ExternalRunnerSendsProtectedInputAndClosesStdin()
    {
        var info = Shell(OperatingSystem.IsWindows() ? "[Console]::Out.Write([Console]::In.ReadToEnd())" : "cat");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await new ExternalProcessRunner().RunWithInput(info, Password + "\n", timeout.Token);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Password + "\n", result.StandardOutput);
        Assert.DoesNotContain(Password, string.Join(' ', info.ArgumentList));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoninteractiveAadCommandsRejectUnsupportedAuthenticationBeforeStarting(bool protectedInput)
    {
        var runner = new DockerSecretTestsInputRunner();
        var service = new ProcessService(NullLogger<ProcessService>.Instance, runner,
            new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance), new DockerSecretTestsSecrets());
        var host = new ServerHost { HostName = "vm", User = "user", AzureBastion = new() { AuthType = BastionAuthType.Aad } };
        await Assert.ThrowsAsync<NotSupportedException>(() => protectedInput
            ? service.RunSshCommandWithInputNonInteractive(host, "sudo -S -p '' docker info", Password + "\n")
            : service.RunSshCommandResultNonInteractive(host, "docker info"));
        Assert.Null(runner.Info);
        Assert.Null(runner.Input);
    }

    [Fact]
    public async Task ProtectedInputCancellationStopsAChildThatNeverReadsStdin()
    {
        var info = Shell(OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 60" : "sleep 60");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ExternalProcessRunner().RunWithInput(info, new string('x', 1_000_000), cancellation.Token));
    }

    private static ProcessStartInfo Shell(string command)
    {
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows()) { info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command"); }
        else info.ArgumentList.Add("-c");
        info.ArgumentList.Add(command);
        return info;
    }
}
