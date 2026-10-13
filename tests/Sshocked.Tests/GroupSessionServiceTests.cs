using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class GroupSessionServiceTests
{
    [Theory]
    [InlineData(ConnectionStrategy.MultiTab)]
    [InlineData(ConnectionStrategy.Sequential)]
    public async Task ConnectionsUseMultiTabIncludingLegacySequentialConfiguration(ConnectionStrategy strategy)
    {
        var dependencies = new GroupSessionTestDependencies();
        var service = new GroupSessionService(dependencies, dependencies, dependencies, dependencies);
        var config = new AppConfig { DefaultConnectionStrategy = strategy };
        await service.ConnectServers(config, [new() { Alias = "server" }]);
        await service.ConnectContainers(config, [new() { Name = "container" }]);
        Assert.Equal(["server:tabs", "container:tabs"], dependencies.Calls);
    }

    [Fact]
    public async Task ServerCommandFailuresDoNotSkipRemainingHostsAndExitStopsTheLoop()
    {
        var dependencies = new GroupSessionTestDependencies("  hostname  ", " ExIt ", "must not execute") { FailureHost = "first" };
        var service = new GroupSessionService(dependencies, dependencies, dependencies, dependencies);
        await service.RunServerCommands([new() { Alias = "first" }, new() { Alias = "second" }]);
        Assert.Equal(["command:first:hostname", "command:second:hostname"], dependencies.Calls);
    }

    [Fact]
    public async Task ContainerCommandLoopTrimsInputAndStopsOnEmptyInput()
    {
        var dependencies = new GroupSessionTestDependencies("  pwd  ", "   ", "must not execute");
        var service = new GroupSessionService(dependencies, dependencies, dependencies, dependencies);
        await service.RunContainerCommands(new(), [new() { Name = "container" }]);
        Assert.Equal(["container-command:pwd"], dependencies.Calls);
    }

    [Fact]
    public async Task ServerOutputIsVisibleBeforeTheRemoteCommandCompletesAndIsNotPrintedTwice()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dependencies = new GroupSessionTestDependencies("hostname", "") { CommandGate = gate };
        var service = new GroupSessionService(dependencies, dependencies, dependencies, dependencies);
        var running = service.RunServerCommands([new() { Alias = "server" }]);
        Assert.False(running.IsCompleted);
        Assert.Contains("live output\n", dependencies.Writes);
        gate.SetResult();
        await running;
        Assert.Equal(1, dependencies.Writes.Count(output => output == "live output\n"));
        Assert.DoesNotContain("buffered output", dependencies.Writes);
    }
}
