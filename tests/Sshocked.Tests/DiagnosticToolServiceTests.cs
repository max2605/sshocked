using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class DiagnosticToolServiceTests
{
    [Fact]
    public async Task VersionsUseOnlyBoundedFixedCommandsAndNeverOpenTerminals()
    {
        var runner = new DiagnosticToolTestRunner();
        var tools = await new DiagnosticToolService(runner, name => name is "ssh" or "az" or "scp" or "wt" ? name + ".exe" : null).Inspect(false);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(["-V"], runner.Calls[0].ArgumentList);
        Assert.Equal(["version", "--output", "json"], runner.Calls[1].ArgumentList);
        Assert.All(runner.Calls, info => { Assert.False(info.UseShellExecute); Assert.True(info.CreateNoWindow); Assert.True(info.RedirectStandardOutput); });
        Assert.Equal(16384, runner.MaximumBytes);
        Assert.Equal("OpenSSH_9.8", tools.Single(tool => tool.Name == "ssh").Version);
        Assert.Equal("2.80.0", tools.Single(tool => tool.Name == "az").Version);
        Assert.All(tools, tool => Assert.Null(tool.Path));
    }

    [Fact]
    public async Task MissingAndTimedOutToolsProduceMetadataWithoutRawProcessErrors()
    {
        var runner = new DiagnosticToolTestRunner { Timeout = true };
        var tools = await new DiagnosticToolService(runner, name => name == "ssh" ? "ssh.exe" : null).Inspect(false);
        Assert.Contains(tools, tool => tool.Name == "az" && !tool.Available);
        Assert.Equal("Version check timed out.", tools.Single(tool => tool.Name == "ssh").Note);
    }
}
