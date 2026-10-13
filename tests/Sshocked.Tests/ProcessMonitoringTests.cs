using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ProcessMonitoringTests
{
    [Fact]
    public void MultiInstanceCountersUseIdentityAndHostCapacity()
    {
        var definition = Definition();
        var first = ProcessMonitoringProvider.Build(ProcessMonitoringProvider.ParseLinux("META\tboot\t10\t100\t2\t1\nPROC\t1\t5\t100\t1024\nPROC\t2\t6\t200\t2048\n"), definition, null);
        Assert.Null(Metric(first, "process.cpuPercent").NumericValue);
        var previous = new MonitoringRecord { State = first.State, Snapshot = first.Snapshot };
        var current = ProcessMonitoringProvider.Build(ProcessMonitoringProvider.ParseLinux("META\tboot\t12\t100\t2\t1\nPROC\t1\t5\t200\t1024\nPROC\t2\t6\t300\t2048\n"), definition, previous);
        Assert.Equal(2, Metric(current, "process.count").NumericValue);
        Assert.Equal(50, Metric(current, "process.cpuPercent").NumericValue);
        Assert.Equal(3072, Metric(current, "process.memoryBytes").NumericValue);
        Assert.False(current.Conditions.Single().Triggered);
    }

    [Theory]
    [InlineData("META\tboot\t12\t100\t2\t1\nPROC\t1\t99\t200\t1024\n")]
    [InlineData("META\treboot\t12\t100\t2\t1\nPROC\t1\t5\t200\t1024\n")]
    [InlineData("META\tboot\t12\t100\t2\t1\nPROC\t1\t5\t50\t1024\n")]
    public void PidReuseRebootAndCounterResetNeverProduceCpuDelta(string current)
    {
        var definition = Definition();
        var first = ProcessMonitoringProvider.Build(ProcessMonitoringProvider.ParseLinux("META\tboot\t10\t100\t2\t1\nPROC\t1\t5\t100\t1024\n"), definition, null);
        var sample = ProcessMonitoringProvider.Build(ProcessMonitoringProvider.ParseLinux(current), definition, new() { State = first.State });
        Assert.Null(Metric(sample, "process.cpuPercent").NumericValue);
    }

    [Fact]
    public void PartialVisibilityNeverProvesAbsenceButKnownExcessStillAlarms()
    {
        var definition = Definition();
        var observation = ProcessMonitoringProvider.Build(ProcessMonitoringProvider.ParseLinux("META\tboot\t10\t100\t2\t0\n"), definition, null);
        Assert.Null(observation.Conditions.Single().Triggered);
        Assert.Equal(MonitorDataQuality.Partial, observation.Snapshot.Quality);
        Assert.Equal("Unknown", Metric(observation, "process.state").TextValue);
        definition.Options["maxCount"] = "0";
        definition.Options["minCount"] = "0";
        observation = ProcessMonitoringProvider.Build(ProcessMonitoringProvider.ParseLinux("META\tboot\t10\t100\t2\t0\nPROC\t1\t5\t100\t1024\n"), definition, null);
        Assert.True(observation.Conditions.Single().Triggered);
    }

    [Fact]
    public void WindowsCimSamplesKeepUnknownResourcesAndMissingCreationDates()
    {
        var sample = ProcessMonitoringProvider.ParseWindows("{\"boot\":\"boot\",\"time\":10,\"cores\":2,\"complete\":true,\"processes\":[{\"pid\":1,\"started\":null,\"cpuTicks\":null,\"memory\":null}]}");
        var observation = ProcessMonitoringProvider.Build(sample, Definition(), null);
        Assert.Equal(1, Metric(observation, "process.count").NumericValue);
        Assert.Null(Metric(observation, "process.cpuPercent").NumericValue);
        Assert.Null(Metric(observation, "process.memoryBytes").NumericValue);
    }

    [Fact]
    public async Task CommandsQuoteSelectorsAndNeverExecuteMutatingActions()
    {
        var fake = new ProcessMonitoringTestTransport { Result = new(0, "META\tboot\t10\t100\t2\t1\n", "") };
        var provider = new ProcessMonitoringProvider(fake);
        var definition = Definition();
        definition.Options["path"] = "/opt/a'quote/app";
        definition.Options["user"] = "tester";
        await provider.Collect(new(), definition, null);
        Assert.Contains("/proc/", fake.Command);
        Assert.Contains("readlink", fake.Command);
        Assert.DoesNotContain("cmdline", fake.Command);
        Assert.DoesNotContain("environ", fake.Command);
        Assert.DoesNotContain("systemctl restart", fake.Command);
        Assert.Contains("'\"'\"'", fake.Command);
        var windows = ServiceMonitoringTests.Definition();
        windows.Options["platform"] = "windows";
        windows.Options["name"] = "service';Write-Error 'injection";
        fake.Result = new(0, "{\"state\":\"Stopped\"}", "");
        await new ServiceMonitoringProvider(fake).Collect(new(), windows, null);
        var script = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(fake.Command.Split(' ').Last()));
        Assert.Contains("$name='service'';Write-Error ''injection'", script);
        Assert.DoesNotContain("Restart-Service", script);
        Assert.DoesNotContain("Stop-Service", script);
    }

    [Fact]
    public async Task TransportFailuresAndCancellationRemainTyped()
    {
        var fake = new ProcessMonitoringTestTransport { Result = new(255, "", "Permission denied") };
        var provider = new ProcessMonitoringProvider(fake);
        var error = await Assert.ThrowsAsync<MonitoringProbeException>(() => provider.Collect(new(), Definition(), null));
        Assert.Equal(MonitorDataQuality.AuthenticationRequired, error.Quality);
        fake.Result = new(127, "", "missing tool");
        error = await Assert.ThrowsAsync<MonitoringProbeException>(() => provider.Collect(new(), Definition(), null));
        Assert.Equal(MonitorDataQuality.Unsupported, error.Quality);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.Collect(new(), Definition(), null, cancellation.Token));
    }

    [Fact]
    public void InvalidSelectorBoundsAreRejectedBeforeRemoteCollection()
    {
        var provider = new ProcessMonitoringProvider(new ProcessMonitoringTestTransport());
        var definition = Definition();
        definition.Options["minCount"] = "3";
        definition.Options["maxCount"] = "2";
        Assert.Throws<ArgumentException>(() => provider.Validate(definition));
        definition.Options["maxCount"] = "4";
        definition.Options["name"] = "bad\nvalue";
        Assert.Throws<ArgumentException>(() => provider.Validate(definition));
    }

    private static MonitorMetric Metric(MonitoringObservation observation, string key) => observation.Snapshot.Metrics.Single(metric => metric.Key == key);
    private static MonitorDefinition Definition() => new() { Name = "Process", Provider = "process", Options = new() { ["name"] = "nginx", ["resources"] = "true" } };
}
