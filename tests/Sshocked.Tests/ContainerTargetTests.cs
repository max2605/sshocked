using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ContainerTargetTests
{
    [Fact]
    public void TagTargetsUseTheirOwnTagsAndReturnHostsAndContainersSeparately()
    {
        var tagged = new ContainerModel { Id = "tagged", Name = "app", Tags = ["web", "production"] };
        var untagged = new ContainerModel { Id = "untagged", Name = "worker", Tags = ["web"] };
        var host = new ServerHost { Id = "server", Tags = ["production", "web"], SavedContainers = [tagged, untagged] };
        var tags = new ServerTagService();
        var targets = tags.FilterTargets([host], [" Production ", "WEB"]);
        Assert.Equal(2, targets.Count);
        Assert.Single(targets, target => target.Container is null);
        Assert.Same(tagged, Assert.Single(targets, target => target.Container is not null).Container);
        Assert.Equal(2, tags.GetAllTargets([host]).Count);
    }

    [Fact]
    public async Task MixedCommandsRunHostCommandOnServerAndQuoteEntireScriptInsideContainer()
    {
        const string command = "printf 'ok'; hostname";
        var host = new ServerHost { Id = "host" };
        var process = new DockerOperationsTestsProcess((_, _, _) => Task.FromResult(new ProcessResult(0, "ok", "")));
        var service = new ContainerGroupService(process, new DockerSecretTestsTerminals(), new DockerSecretTestsConsoleHelper(),
            new DockerSecretTestsSecrets(), NullLogger<ContainerGroupService>.Instance);
        var results = await service.RunOnTargets([new(host, null), new(host, new() { Name = "app" })], command);
        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.Success));
        Assert.Contains(command, process.Commands);
        Assert.Contains("docker exec -- 'app' sh -lc 'printf '\\''ok'\\''; hostname'", process.Commands);
    }

    [Fact]
    public void ParentResolutionDoesNotMatchUnrelatedContainersWithNullSavedIds()
    {
        var host = new ServerHost { Id = "host", SavedContainers = [new() { Name = "other" }] };
        var process = new DockerOperationsTestsProcess((_, _, _) => throw new NotSupportedException());
        var service = new ContainerGroupService(process, new DockerSecretTestsTerminals(), new DockerSecretTestsConsoleHelper(),
            new DockerSecretTestsSecrets(), NullLogger<ContainerGroupService>.Instance);
        Assert.Null(service.FindParentServer(new() { Hosts = [host] }, new() { Name = "app" }));
    }

    [Fact]
    public async Task PermissionErrorInsideContainerDoesNotRepeatACommandViaSudo()
    {
        var host = new ServerHost { Id = "host" };
        var process = new DockerOperationsTestsProcess((_, _, _) => Task.FromResult(new ProcessResult(1, "side effect already happened", "sh: protected-file: Permission denied")));
        var service = new ContainerGroupService(process, new DockerSecretTestsTerminals(), new DockerSecretTestsConsoleHelper(),
            new DockerSecretTestsSecrets(), NullLogger<ContainerGroupService>.Instance);
        var results = await service.RunOnTargets([new(host, new() { Name = "app" })], "printf data >> log; cat protected-file");
        Assert.False(Assert.Single(results).Success);
        Assert.Single(process.Commands);
    }

    [Fact]
    public async Task MixedCommandOutputLimitReportsOneFailureAndKeepsOtherResults()
    {
        var host = new ServerHost { Id = "host" };
        var process = new DockerOperationsTestsProcess((_, command, _) => command == "flood"
            ? throw new ProcessOutputLimitException(16_777_216)
            : Task.FromResult(new ProcessResult(0, "done", "")));
        var service = new ContainerGroupService(process, new DockerSecretTestsTerminals(), new DockerSecretTestsConsoleHelper(),
            new DockerSecretTestsSecrets(), NullLogger<ContainerGroupService>.Instance);
        var results = await service.RunOnTargets([new(host, null), new(host, new() { Name = "app" })], "flood");
        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);
        Assert.Contains("capture limit", results[0].Error);
        Assert.True(results[1].Success);
    }
}
