using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ServiceMonitoringTests
{
    [Theory]
    [InlineData("loaded", "active", "running", "Running", false)]
    [InlineData("loaded", "active", "exited", "Running", false)]
    [InlineData("loaded", "inactive", "dead", "Stopped", true)]
    [InlineData("loaded", "failed", "failed", "Failed", true)]
    [InlineData("loaded", "activating", "start", "Starting", true)]
    [InlineData("not-found", "inactive", "dead", "Missing", true)]
    [InlineData("masked", "inactive", "dead", "Masked", true)]
    public void SystemdStatesPreserveOneshotAndMissingDistinctions(string load, string active, string substate, string expected, bool mismatch)
    {
        var observation = ServiceMonitoringProvider.ParseLinux($"Id=nginx.service\nLoadState={load}\nActiveState={active}\nSubState={substate}\n", Definition(), null);
        Assert.Equal(expected, observation.Snapshot.Metrics.Single(metric => metric.Key == "service.state").TextValue);
        Assert.Equal(mismatch, observation.Conditions.Single(condition => condition.Key == "service.expected-state").Triggered);
    }

    [Theory]
    [InlineData("Running", "Running")]
    [InlineData("Stopped", "Stopped")]
    [InlineData("Start Pending", "Starting")]
    [InlineData("Stop Pending", "Stopping")]
    [InlineData("Paused", "Paused")]
    public void WindowsServiceNativeStatesAreMappedWithoutInferringRestartNeed(string native, string expected)
    {
        var observation = ServiceMonitoringProvider.ParseWindows("{\"state\":\"" + native + "\",\"startMode\":\"Auto\"}", Definition(), null);
        Assert.Equal(expected, observation.Snapshot.Metrics.Single(metric => metric.Key == "service.state").TextValue);
        Assert.Equal("Unknown", observation.Snapshot.Metrics.Single(metric => metric.Key == "service.restartRequirement").TextValue);
        Assert.DoesNotContain(observation.Conditions, condition => condition.Key == "service.restart-required");
    }

    [Fact]
    public void RestartMarkersAndDaemonReloadStaySeparateAndNeedrestartMatchesExactUnit()
    {
        var definition = Definition();
        var observation = ServiceMonitoringProvider.ParseLinux("Id=nginx.service\nLoadState=loaded\nActiveState=active\nMarkers=needs-restart\nNeedDaemonReload=no\n", definition, null);
        Assert.True(observation.Conditions.Single(condition => condition.Key == "service.restart-required").Triggered);
        Assert.False(observation.Conditions.Single(condition => condition.Key == "service.reload-required").Triggered);
        observation = ServiceMonitoringProvider.ParseLinux("Id=nginx.service\nLoadState=loaded\nActiveState=active\nMarkers=\nNeedDaemonReload=yes\n", definition, null);
        Assert.False(observation.Conditions.Single(condition => condition.Key == "service.restart-required").Triggered);
        Assert.True(observation.Conditions.Single(condition => condition.Key == "service.reload-required").Triggered);
        definition.Options["needrestart"] = "true";
        observation = ServiceMonitoringProvider.ParseLinux("Id=nginx.service\nActiveState=active\nNEEDRESTART-SVC: other.service\nSSK_NEEDRESTART=success\n", definition, null);
        Assert.False(observation.Conditions.Single(condition => condition.Key == "service.restart-required").Triggered);
        observation = ServiceMonitoringProvider.ParseLinux("Id=nginx.service\nActiveState=active\nNEEDRESTART-SVC: nginx.service\nSSK_NEEDRESTART=success\n", definition, null);
        Assert.True(observation.Conditions.Single(condition => condition.Key == "service.restart-required").Triggered);
    }

    [Fact]
    public void ExpectedStoppedAndUnknownNativeStatesHaveDifferentSemantics()
    {
        var definition = Definition();
        definition.Options["expected"] = "stopped";
        var observation = ServiceMonitoringProvider.ParseLinux("LoadState=loaded\nActiveState=inactive\n", definition, null);
        Assert.False(observation.Conditions.Single().Triggered);
        observation = ServiceMonitoringProvider.ParseLinux("LoadState=loaded\nActiveState=future-state\n", definition, null);
        Assert.Null(observation.Conditions.Single().Triggered);
        Assert.Equal(MonitorDataQuality.Partial, observation.Snapshot.Quality);
    }

    [Fact]
    public void ServiceCpuUsesBaselineAndResetsAfterInvocationChange()
    {
        var definition = Definition();
        definition.Options["resources"] = "true";
        string Sample(string invocation, long cpu, int time) => $"Id=nginx.service\nActiveState=active\nMainPID=50\nInvocationID={invocation}\nSSK_BOOT=boot\nSSK_CORES=2\nSSK_TIME={time}\nCPUUsageNSec={cpu}\nMemoryCurrent=1024\n";
        var first = ServiceMonitoringProvider.ParseLinux(Sample("a", 2_000_000_000, 10), definition, null);
        Assert.Null(first.Snapshot.Metrics.Single(metric => metric.Key == "service.cpuPercent").NumericValue);
        var previous = new MonitoringRecord { Snapshot = first.Snapshot, State = first.State };
        var second = ServiceMonitoringProvider.ParseLinux(Sample("a", 4_000_000_000, 12), definition, previous);
        Assert.Equal(50, second.Snapshot.Metrics.Single(metric => metric.Key == "service.cpuPercent").NumericValue);
        var changed = ServiceMonitoringProvider.ParseLinux(Sample("b", 4_000_000_000, 12), definition, previous);
        Assert.Null(changed.Snapshot.Metrics.Single(metric => metric.Key == "service.cpuPercent").NumericValue);
    }

    [Fact]
    public void GracePeriodDelaysStateAlarmAndUnknownCannotRecoverIt()
    {
        var definition = Definition();
        definition.GraceSeconds = 30;
        var evaluator = new MonitorAlertEvaluator();
        var record = new MonitoringRecord();
        var now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
        evaluator.Evaluate(definition, ServiceMonitoringProvider.ParseLinux("ActiveState=activating\n", definition, null), record, now, 20);
        Assert.Empty(record.Events);
        evaluator.Evaluate(definition, ServiceMonitoringProvider.ParseLinux("ActiveState=failed\n", definition, null), record, now.AddSeconds(31), 20);
        Assert.Single(record.Events);
        evaluator.Evaluate(definition, ServiceMonitoringProvider.ParseLinux("ActiveState=future\n", definition, null), record, now.AddSeconds(60), 20);
        Assert.True(record.Alarms.Single().Active);
        evaluator.Evaluate(definition, ServiceMonitoringProvider.ParseLinux("ActiveState=active\n", definition, null), record, now.AddSeconds(61), 20);
        Assert.Equal(["raised", "recovered"], record.Events.Select(item => item.Action));
    }

    internal static MonitorDefinition Definition() => new() { Name = "Service", Provider = "service", Options = new() { ["name"] = "nginx.service" } };
}
