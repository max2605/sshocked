using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ConnectionArgumentTests
{
    [Fact]
    public void ChildSessionsSelectStableIdsInsteadOfBuildingRemoteShellCommands()
    {
        var parsed = new ArgumentParserService().Parse(["--host-id", "host-id", "--container-id", "container-id"]);
        Assert.False(parsed.IsInteractive);
        Assert.Equal("host-id", parsed.TargetHostId);
        Assert.Equal("container-id", parsed.TargetContainerId);
        Assert.Throws<ArgumentException>(() => new ArgumentParserService().Parse(["--host-id", "host-id", "extra"]));
        if (!OperatingSystem.IsWindows()) return;
        var info = TerminalLauncherService.BuildStartInfo("C:\\Application Path\\ssk.exe", ["--host-id", "host-id"], "title; & text");
        Assert.Contains("C:\\Application Path\\ssk.exe", info.ArgumentList);
        Assert.Contains("title; & text", info.ArgumentList);
        Assert.Contains("--host-id", info.ArgumentList);
        Assert.Empty(info.Arguments);
    }
}
