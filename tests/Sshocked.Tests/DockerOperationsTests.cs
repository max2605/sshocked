using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class DockerOperationsTests
{
    [Theory]
    [InlineData(DockerOperation.Start, "start")]
    [InlineData(DockerOperation.Stop, "stop")]
    [InlineData(DockerOperation.Restart, "restart")]
    [InlineData(DockerOperation.Pause, "pause")]
    [InlineData(DockerOperation.Unpause, "unpause")]
    [InlineData(DockerOperation.Remove, "rm")]
    public async Task OperationsPinStoredIdentityAndQuoteItWithoutForcingRemoval(DockerOperation operation, string verb)
    {
        var container = new ContainerModel { ContainerId = "id'; touch sentinel; #", Name = "different-name" };
        var host = new ServerHost { Id = "host", DockerInfo = new() { LastScannedAt = DateTime.UtcNow, Containers = [container] }, SavedContainers = [container] };
        var process = new DockerOperationsTestsProcess((_, _, _) => Task.FromResult(new ProcessResult(0, "done\n", "")));
        var result = await Service(host, process).Operate(host, container, operation, true);
        Assert.True(result.Success);
        Assert.Equal($"docker {verb} -- 'id'\\''; touch sentinel; #'", Assert.Single(process.Commands));
        Assert.Null(host.DockerInfo.LastScannedAt);
        Assert.Single(host.SavedContainers);
        if (operation == DockerOperation.Remove) Assert.Empty(host.DockerInfo.Containers);
    }

    [Fact]
    public async Task RemovalWithoutConfirmationDoesNotInvokeSsh()
    {
        var host = new ServerHost { Id = "host" };
        var process = new DockerOperationsTestsProcess((_, _, _) => throw new InvalidOperationException("Must not run"));
        var result = await Service(host, process).Operate(host, new() { Name = "app" }, DockerOperation.Remove);
        Assert.False(result.Success);
        Assert.Contains("confirmation", result.Error);
        Assert.Empty(process.Commands);
    }

    [Fact]
    public async Task BatchConcurrencyIsBoundedAndPartialFailuresRetainEveryTargetResult()
    {
        var host = new ServerHost { Id = "host" };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        var active = 0;
        var peak = 0;
        var process = new DockerOperationsTestsProcess(async (_, command, ct) =>
        {
            lock (gate) { active++; peak = Math.Max(peak, active); if (active == 2) twoStarted.TrySetResult(); }
            try
            {
                await release.Task.WaitAsync(ct);
                return command.Contains("'broken'", StringComparison.Ordinal) ? new(1, "", "No such container: broken") : new(0, "done", "");
            }
            finally { lock (gate) active--; }
        });
        var targets = new[] { "one", "broken", "three", "four" }.Select(name => new ConnectionTarget(host, new() { Name = name })).ToArray();
        var batch = Service(host, process).OperateMany(targets, DockerOperation.Start, maxConcurrency: 2);
        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, process.Commands.Count);
        release.SetResult();
        var results = await batch;
        Assert.Equal(2, peak);
        Assert.Equal(4, results.Count);
        Assert.Equal(3, results.Count(result => result.Success));
        Assert.Contains("no longer exists", results[1].Error);
    }

    [Fact]
    public async Task BatchCancellationStopsRunningCommandsAndReportsQueuedTargets()
    {
        var host = new ServerHost { Id = "host" };
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new DockerOperationsTestsProcess(async (_, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new(0, "", "");
        });
        var targets = new[] { "one", "two", "three" }.Select(name => new ConnectionTarget(host, new() { Name = name })).ToArray();
        var batch = Service(host, process).OperateMany(targets, DockerOperation.Stop, maxConcurrency: 1, ct: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var results = await batch;
        Assert.Single(process.Commands);
        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.Equal(DockerActionState.Cancelled, result.State));
    }

    [Fact]
    public async Task ScanSeparatesStderrFromJsonAndAcceptsComposeArraysAndMissingLoginMemory()
    {
        var host = new ServerHost { Id = "host", AuthType = AuthType.Password };
        var process = new DockerOperationsTestsProcess((_, command, _) => Task.FromResult(command.Contains("docker info", StringComparison.Ordinal)
            ? new ProcessResult(0, "{\"ServerVersion\":\"28\",\"Containers\":1}", "harmless warning")
            : command.Contains("docker ps", StringComparison.Ordinal)
                ? new ProcessResult(0, "{\"ID\":\"full-id\",\"Names\":\"/app\",\"ComposeProject\":\"project\"}", "")
                : new ProcessResult(0, "[{\"Name\":\"project\",\"Status\":\"running(1)\"}]", "")));
        var info = await Service(host, process).Scan(host);
        Assert.True(info.DockerAvailable);
        Assert.Null(info.ScanError);
        Assert.Equal("app", Assert.Single(info.Containers).Name);
        Assert.Equal("project", Assert.Single(info.Containers).ComposeProject);
        Assert.Equal("project", Assert.Single(info.ComposeStacks).Name);
        Assert.DoesNotContain(process.Commands, command => command.Contains("2>/dev/null", StringComparison.Ordinal) || command.Contains("||", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DirectDockerAccessFallsBackOnlyForPermissionsAndAuthenticationErrorsAreActionable()
    {
        var host = new ServerHost { Id = "host" };
        var process = new DockerOperationsTestsProcess((_, command, _) => Task.FromResult(command.StartsWith("sudo", StringComparison.Ordinal)
            ? new ProcessResult(1, "", "sudo: a password is required") : new ProcessResult(1, "", "permission denied while connecting to Docker")));
        var info = await Service(host, process).Scan(host);
        Assert.Contains("sudo", info.ScanError);
        Assert.Equal(2, process.Commands.Count);
        var sshFailure = new DockerOperationsTestsProcess((_, _, _) => Task.FromResult(new ProcessResult(255, "", "Permission denied (password)")));
        info = await Service(host, sshFailure).Scan(host);
        Assert.Contains("SSH", info.ScanError);
        Assert.Single(sshFailure.Commands);
    }

    [Fact]
    public async Task ResolvedSudoPasswordIsRedactedFromOperationOutputAndNeverEntersArguments()
    {
        var host = new ServerHost { Id = "host", SudoPasswordSecretRef = new() { SecretId = "sudo", SecretType = SecretType.Password } };
        var process = new DockerOperationsTestsProcess((_, _, _) => Task.FromResult(new ProcessResult(0, DockerSecretTests.Password, "")));
        var result = await Service(host, process).Operate(host, new() { Name = "app" }, DockerOperation.Start);
        Assert.True(result.Success);
        Assert.DoesNotContain(DockerSecretTests.Password, result.Output);
        Assert.DoesNotContain(DockerSecretTests.Password, Assert.Single(process.Commands));
        Assert.Equal(DockerSecretTests.Password + "\n", Assert.Single(process.Inputs));
        Assert.Null(host.SudoPassword);
    }

    private static DockerService Service(ServerHost host, DockerOperationsTestsProcess process) =>
        new(process, new DockerSecretTestsRepository(host), new DockerSecretTestsSecrets(), NullLogger<DockerService>.Instance);

    [Fact]
    public async Task OutputLimitFailureIsVisibleAndDoesNotDiscardSuccessfulBatchTargets()
    {
        var host = new ServerHost { Id = "host", SudoPassword = DockerSecretTests.Password };
        var process = new DockerOperationsTestsProcess((_, command, _) => command.Contains("'flood'", StringComparison.Ordinal)
            ? throw new ProcessOutputLimitException(16_777_216)
            : Task.FromResult(new ProcessResult(0, "done", "")));
        var results = await Service(host, process).OperateMany(
            [new(host, new() { Name = "flood" }), new(host, new() { Name = "app" })], DockerOperation.Restart);
        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);
        Assert.Contains("capture limit", results[0].Error);
        Assert.Contains("may already have changed", results[0].Error);
        Assert.True(results[1].Success);
        Assert.Equal(2, process.Inputs.Count);
    }

    [Fact]
    public async Task DiscoveryOutputLimitDoesNotReportASuccessfulEmptyScan()
    {
        var host = new ServerHost { Id = "host" };
        var process = new DockerOperationsTestsProcess((_, _, _) => throw new ProcessOutputLimitException(16_777_216));
        var info = await Service(host, process).Scan(host);
        Assert.False(info.DockerAvailable);
        Assert.Contains("capture limit", info.ScanError);
        Assert.Single(process.Commands);
    }
}
