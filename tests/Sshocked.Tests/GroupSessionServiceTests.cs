using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class GroupSessionServiceTests
{
    [Theory]
    [InlineData(ConnectionStrategy.MultiTab, "server:tabs", "container:tabs")]
    [InlineData(ConnectionStrategy.Sequential, "server:sequence", "container:sequence")]
    public async Task ConnectionsUseConfiguredStrategy(ConnectionStrategy strategy, string serverCall, string containerCall)
    {
        var dependencies = new GroupSessionTestDependencies();
        var service = new GroupSessionService(dependencies, dependencies, dependencies, dependencies);
        var config = new AppConfig { DefaultConnectionStrategy = strategy };
        await service.ConnectServers(config, [new() { Alias = "server" }]);
        await service.ConnectContainers(config, [new() { Name = "container" }]);
        Assert.Equal([serverCall, containerCall], dependencies.Calls);
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
}
