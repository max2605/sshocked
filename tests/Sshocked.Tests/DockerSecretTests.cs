using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class DockerSecretTests
{
    private const string Password = "super-private-sudo-password";

    [Fact]
    public async Task PasswordSecretHostIsScannedThroughNoninteractiveSsh()
    {
        var host = new ServerHost { Id = "host", Alias = "vm", User = "user", HostName = "vm", AuthType = AuthType.Password,
            PasswordSecretRef = new() { SecretId = "login-password", SecretType = SecretType.Password } };
        var repository = new Repository(host);
        var process = new ScanProcess();
        var service = new DockerService(process, repository, new Secrets(), NullLogger<DockerService>.Instance);
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
        var process = new ScanProcess();
        var result = await new DockerService(process, new Repository(host), new Secrets(), NullLogger<DockerService>.Instance).Scan(host);
        Assert.True(result.DockerAvailable);
        Assert.Equal(3, process.Commands.Count);
        Assert.All(process.Commands, command => Assert.DoesNotContain(Password, command));
    }

    [Fact]
    public async Task DockerCachePreservesConcurrentHostIdentityAndSecretEdits()
    {
        var host = new ServerHost { Id = "host", Alias = "vm", User = "user", HostName = "vm", IdentityFile = "old-key" };
        var repository = new ConcurrentRepository(host);
        var result = await new DockerService(new ScanProcess(), repository, new Secrets(), NullLogger<DockerService>.Instance).Scan(host);
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
        var process = new ScanProcess();
        var secrets = new Secrets();
        var service = new DockerService(process, new Repository(host), secrets, NullLogger<DockerService>.Instance);
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
        var process = new ScanProcess();
        var service = new ContainerGroupService(process, new Terminals(), new ConsoleHelper(), new Secrets(), NullLogger<ContainerGroupService>.Instance);
        await service.RunCommandOnAll(config, [container], "id");
        Assert.Equal(Password + "\n", Assert.Single(process.Inputs));
        Assert.Equal("sudo -S -p '' docker exec -- 'name'\\''; touch sentinel; #' id", Assert.Single(process.Commands));
        Assert.DoesNotContain(Password, process.Commands[0]);
    }

    [Fact]
    public async Task SudoSecretIsSentOnlyAsStdinAndNeverStoredOrLogged()
    {
        var host = new ServerHost { Id = "host", Alias = "vm", User = "user", HostName = "vm",
            SudoPasswordSecretRef = new() { SecretId = "sudo-password", SecretType = SecretType.Password } };
        var repository = new Repository(host);
        var process = new ScanProcess();
        var secrets = new Secrets();
        var log = new CaptureLogger<DockerService>();
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
        var repository = new Repository(host);
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-memory-sudo-tests-" + Guid.NewGuid().ToString("N"));
        var auditPath = Path.Combine(directory, "audit.jsonl");
        var runner = new AuditScanRunner();
        var processLog = new CaptureLogger<ProcessService>();
        var dockerLog = new CaptureLogger<DockerService>();
        try
        {
            using var audit = new AuditService(repository, NullLogger<AuditService>.Instance, auditPath);
            var process = new ProcessService(processLog, runner, new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance), new Secrets(), audit);
            var result = await new DockerService(process, repository, new Secrets(), dockerLog).Scan(host);
            Assert.True(result.DockerAvailable);
            Assert.Equal(3, runner.Inputs.Count);
            Assert.All(runner.Inputs, input => Assert.Equal(password + "\n", input));
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

    [Fact]
    public async Task ProtectedCommandKeepsCredentialOutsideSshArguments()
    {
        var runner = new InputRunner();
        var logger = new CaptureLogger<ProcessService>();
        var service = new ProcessService(logger, runner, new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance), new Secrets());
        var host = new ServerHost { User = "user", HostName = "vm" };
        var result = await service.RunSshCommandWithInput(host, "sudo -S -p '' docker info", Password + "\n");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Password + "\n", runner.Input);
        Assert.DoesNotContain(Password, string.Join(' ', runner.Info!.ArgumentList));
        Assert.DoesNotContain(Password, string.Join('\n', logger.Lines));
        Assert.DoesNotContain(Password, runner.Info.Arguments);
    }

    [Fact]
    public async Task ExternalRunnerSendsProtectedInputAndClosesStdin()
    {
        var info = Shell(OperatingSystem.IsWindows() ? "[Console]::Out.Write([Console]::In.ReadToEnd())" : "cat");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await new ExternalProcessRunner().RunWithInputAsync(info, Password + "\n", timeout.Token);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Password + "\n", result.StandardOutput);
        Assert.DoesNotContain(Password, string.Join(' ', info.ArgumentList));
    }

    [Fact]
    public async Task ProtectedInputCancellationStopsAChildThatNeverReadsStdin()
    {
        var info = Shell(OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 60" : "sleep 60");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ExternalProcessRunner().RunWithInputAsync(info, new string('x', 1_000_000), cancellation.Token));
    }

    private static ProcessStartInfo Shell(string command)
    {
        var info = new ProcessStartInfo { FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh", UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (OperatingSystem.IsWindows()) { info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command"); }
        else info.ArgumentList.Add("-c");
        info.ArgumentList.Add(command);
        return info;
    }

    private sealed class Repository(ServerHost host) : IConfigRepository
    {
        public AppConfig Config { get; } = new() { Hosts = [host] };
        public AppConfig Load() => Config;
        public void Save(AppConfig config) { }
    }

    private sealed class ConcurrentRepository(ServerHost host) : IConfigRepository
    {
        public AppConfig Config { get; private set; } = new() { Hosts = [new ServerHost { Id = host.Id, IdentityFile = host.IdentityFile }] };
        public AppConfig Load()
        {
            var snapshot = JsonSerializer.Deserialize(JsonSerializer.Serialize(Config, AppConfigContext.Default.AppConfig), AppConfigContext.Default.AppConfig)!;
            ConcurrentEdit();
            return snapshot;
        }
        public void Save(AppConfig config) => Config = config;
        public void Update(Action<AppConfig> update) { ConcurrentEdit(); update(Config); }
        private void ConcurrentEdit()
        {
            Config.Hosts[0].IdentityFile = "rotated-key";
            Config.Hosts[0].KeySecretRef = new() { SecretId = "rotated-secret", SecretType = SecretType.SshPrivateKey };
        }
    }

    private sealed class ScanProcess : IProcessService
    {
        public List<string> Commands { get; } = [];
        public List<string> Inputs { get; } = [];
        public Task RunSsh(ServerHost host) => Task.CompletedTask;
        public Task RunSshInteractive(ServerHost host, string command) { Commands.Add(command); return Task.CompletedTask; }
        public Task<string> RunSshCommand(ServerHost host, string command)
        { Commands.Add(command); return Task.FromResult(Output(command)); }
        public Task<ProcessResult> RunSshCommandWithInput(ServerHost host, string command, string input, CancellationToken ct = default)
        { Commands.Add(command); Inputs.Add(input); return Task.FromResult(new ProcessResult(0, Output(command), "")); }
        private static string Output(string command) => command.Contains("docker info", StringComparison.Ordinal)
            ? "{\"ServerVersion\":\"27.0.0\",\"OSType\":\"linux\",\"Architecture\":\"x86_64\",\"Containers\":0,\"ContainersRunning\":0}" : "";
    }

    private sealed class Secrets : ISecretProviderRegistry
    {
        public int ResolveCount { get; private set; }
        public IReadOnlyList<SecretProviderConfig> GetConfigurations() => [];
        public ISecretProvider GetProvider(string? name = null) => throw new NotSupportedException();
        public Task<SecretResult> ResolveAsync(SecretRef reference, CancellationToken ct = default)
        { ResolveCount++; return Task.FromResult(new SecretResult { Success = true, Value = Password }); }
        public void ClearCache() { }
    }

    private sealed class Terminals : ITerminalLauncherService
    {
        public Task Launch(ServerHost host, ContainerModel? container = null) => throw new NotSupportedException();
    }

    private sealed class ConsoleHelper : IConsoleHelperService
    {
        public void WaitForKey() => throw new NotSupportedException();
        public Task RunSsh(ServerHost host) => throw new NotSupportedException();
        public Task RunDockerExec(ServerHost host, ContainerModel container) => throw new NotSupportedException();
        public Task RunDockerLogs(ServerHost host, ContainerModel container) => throw new NotSupportedException();
    }

    private sealed class InputRunner : IExternalProcessRunner
    {
        public ProcessStartInfo? Info { get; private set; }
        public string? Input { get; private set; }
        public Process Start(ProcessStartInfo startInfo) => throw new NotSupportedException();
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RunWithInputAsync(ProcessStartInfo startInfo, string input, CancellationToken cancellationToken = default)
        { Info = startInfo; Input = input; return Task.FromResult(new ProcessResult(0, "{}", "")); }
    }

    private sealed class AuditScanRunner : IExternalProcessRunner
    {
        public List<string> Commands { get; } = [];
        public List<string> Inputs { get; } = [];
        public Process Start(ProcessStartInfo startInfo) => throw new NotSupportedException();
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
        {
            var command = startInfo.ArgumentList[^1];
            Commands.Add(command);
            var output = command.Contains("docker info", StringComparison.Ordinal) ? "{\"ServerVersion\":\"27.0.0\"}" : "";
            return Task.FromResult(new ProcessResult(0, output, ""));
        }
        public Task<ProcessResult> RunWithInputAsync(ProcessStartInfo startInfo, string input, CancellationToken cancellationToken = default)
        {
            Inputs.Add(input);
            return RunAsync(startInfo, cancellationToken);
        }
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
