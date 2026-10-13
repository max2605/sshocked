using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ServerMonitoringProviderTests
{
    private const string Linux = """
        BOOT boot1
        cpu 100 0 100 800 0 0 0 0 40 0
        MemTotal: 1000
        MemAvailable: 500
        SwapTotal: 100
        SwapFree: 90
        UPTIME 300
        FS_BEGIN
        Filesystem 1024-blocks Used Available Capacity Mounted on
        /dev/sda 1000 500 500 50% /
        """;

    [Fact]
    public void CpuUsesCounterDeltasAndDoesNotDoubleCountGuestTime()
    {
        var first = ServerMonitoringProvider.ParseLinux(Linux, null);
        Assert.Null(first.Snapshot.Metrics[0].NumericValue);
        var next = Linux.Replace("cpu 100 0 100 800 0 0 0 0 40 0", "cpu 200 0 200 1600 0 0 0 0 10000 0");
        var second = ServerMonitoringProvider.ParseLinux(next, new() { State = first.State });
        Assert.Equal(20, second.Snapshot.Metrics[0].NumericValue!.Value, 8);
        Assert.Equal(50, second.Snapshot.Metrics.Single(metric => metric.Key == "memory.percent").NumericValue);
    }

    [Fact]
    public void RebootAndCounterResetNeedANewBaseline()
    {
        var first = ServerMonitoringProvider.ParseLinux(Linux, null);
        var previous = new MonitoringRecord { State = first.State };
        Assert.Null(ServerMonitoringProvider.ParseLinux(Linux.Replace("boot1", "boot2"), previous).Snapshot.Metrics[0].NumericValue);
        Assert.Null(ServerMonitoringProvider.ParseLinux(Linux.Replace("100 0 100 800", "1 0 1 8"), previous).Snapshot.Metrics[0].NumericValue);
    }

    [Fact]
    public void WindowsNullMetricsAreUnsupportedWithoutInventedZeros()
    {
        var observation = ServerMonitoringProvider.ParseWindows("""{"cpu":null,"total":1024,"available":null,"uptime":120,"volumes":[]}""");
        Assert.Null(observation.Snapshot.Metrics[0].NumericValue);
        Assert.Equal(MonitorDataQuality.Unsupported, observation.Snapshot.Metrics[0].Quality);
        Assert.Equal(MonitorDataQuality.Partial, observation.Snapshot.Quality);
    }

    [Fact]
    public void SmartHealthWarnExitStillCarriesValidData()
    {
        var observation = SmartMonitoringProvider.ParseLinux("""{"smartctl":{"exit_status":8},"smart_status":{"passed":false},"temperature":{"current":40}}""", 8);
        Assert.Equal(MonitorDataQuality.Success, observation.Snapshot.Quality);
        Assert.True(observation.Conditions.Single(condition => condition.Key == "smart.failed").Triggered);
        Assert.Equal(40, observation.Snapshot.Metrics.Single(metric => metric.Key == "smart.temperature").NumericValue);
    }

    [Fact]
    public void MissingSmartCapabilitiesRemainUnknown()
    {
        var observation = SmartMonitoringProvider.ParseLinux("""{"smartctl":{"exit_status":0}}""", 0);
        Assert.Null(observation.Conditions.Single(condition => condition.Key == "smart.failed").Triggered);
        Assert.Equal(MonitorDataQuality.Partial, observation.Snapshot.Quality);
        var windows = SmartMonitoringProvider.ParseWindows("""{"health":"Healthy","temperature":null,"wear":null}""");
        Assert.Equal(MonitorDataQuality.Partial, windows.Snapshot.Quality);
    }

    [Fact]
    public void MissingOrOldPackageIndicesAreNotAnUpToDateSystem()
    {
        var definition = new MonitorDefinition();
        var now = DateTimeOffset.FromUnixTimeSeconds(1000000);
        var missing = UpdateMonitoringProvider.ParseLinux("UPDATES 0\nREBOOT 0\nINDEX\n", definition, now);
        Assert.Null(missing.Snapshot.Metrics[0].NumericValue);
        Assert.Equal(MonitorDataQuality.Partial, missing.Snapshot.Quality);
        var stale = UpdateMonitoringProvider.ParseLinux("UPDATES 4\nREBOOT 1\nINDEX 1\n", definition, now);
        Assert.True(stale.Conditions.Single(condition => condition.Key == "updates.indexStale").Triggered);
        Assert.True(stale.Conditions.Single(condition => condition.Key == "updates.reboot").Triggered);
    }

    [Fact]
    public async Task LinuxProbeScriptsParseWithoutExecutingSystemChanges()
    {
        foreach (var script in new[] { ServerMonitorCommands.Linux, UpdateMonitorCommands.Linux })
        {
            var info = LocalShellTestRunner.ShellInfo();
            info.ArgumentList.Add("-n");
            info.RedirectStandardInput = true;
            var result = await new ExternalProcessRunner().RunWithInput(info, script);
            Assert.Equal(0, result.ExitCode);
        }
    }
}
