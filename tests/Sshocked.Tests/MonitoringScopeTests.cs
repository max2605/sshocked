using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MonitoringScopeTests
{
    [Fact]
    public async Task EndpointChangesInvalidateStatusAndResetBaselinesAndAlarms()
    {
        using var fixture = new MonitoringTestFixture();
        var definition = fixture.Add();
        fixture.Repository.Update(config => config.Monitoring.Monitors[0].Rules.Add(new() { MetricKey = "cpu.percent", Threshold = 70 }));
        await fixture.Service.Run();
        Assert.Equal(MonitorSeverity.Warning, Assert.Single(fixture.Service.Status()).Severity);
        fixture.Repository.Update(config => config.Hosts[0].HostName = "other.invalid");
        var stale = Assert.Single(fixture.Service.Status());
        Assert.Equal(MonitorDataQuality.Stale, stale.Quality);
        Assert.Equal(MonitorSeverity.Unknown, stale.Severity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.Acknowledge(definition.Id,
            fixture.Store.Load(definition.Id)!.Alarms[0].Key));
        fixture.Provider.InspectPrevious = previous =>
        {
            Assert.Empty(previous!.Alarms);
            Assert.Empty(previous.State);
        };
        fixture.Provider.Work = (_, _) => Task.FromResult(MonitoringTestProvider.Value(10));
        await fixture.Service.Run();
        var record = fixture.Store.Load(definition.Id)!;
        Assert.Equal(MonitorSeverity.Ok, record.Snapshot.Severity);
        Assert.Contains(record.Events, entry => entry.Action == "scope.changed");
        Assert.DoesNotContain(record.Events, entry => entry.Action == "recovered");
    }

    [Fact]
    public async Task MetadataChangesKeepAValidProbeButRuleChangesInvalidateIt()
    {
        using var fixture = new MonitoringTestFixture();
        fixture.Add();
        await fixture.Service.Run();
        fixture.Repository.Update(config =>
        {
            config.Hosts[0].IsFavorite = true;
            config.Hosts[0].Tags.Add("production");
            config.Hosts[0].DockerInfo = new();
        });
        Assert.Equal(MonitorDataQuality.Success, Assert.Single(fixture.Service.Status()).Quality);
        fixture.Repository.Update(config => config.Monitoring.Monitors[0].Rules.Add(new() { MetricKey = "cpu.percent", Threshold = 70 }));
        Assert.Equal(MonitorDataQuality.Stale, Assert.Single(fixture.Service.Status()).Quality);
    }

    [Fact]
    public async Task AConfigurationChangeDuringCollectionCannotPublishOrSaveSuccess()
    {
        using var fixture = new MonitoringTestFixture();
        var definition = fixture.Add();
        fixture.Provider.Work = (_, _) =>
        {
            fixture.Repository.Update(config => config.Hosts[0].Port = 2222);
            return Task.FromResult(MonitoringTestProvider.Value(10));
        };
        var snapshot = Assert.Single(await fixture.Service.Run());
        Assert.Equal(MonitorDataQuality.Stale, snapshot.Quality);
        Assert.Equal(MonitorSeverity.Unknown, snapshot.Severity);
        Assert.Null(fixture.Store.Load(definition.Id));
    }

    [Fact]
    public async Task DeleteWaitsForAnActiveProbeAndLeavesNoRuntimeResult()
    {
        using var fixture = new MonitoringTestFixture();
        var definition = fixture.Add();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Work = async (_, ct) =>
        {
            started.SetResult();
            await finish.Task.WaitAsync(ct);
            return MonitoringTestProvider.Value(10);
        };
        var run = fixture.Service.Run();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var delete = fixture.Service.Delete(definition.Id);
        Assert.False(delete.IsCompleted);
        finish.SetResult();
        await run;
        await delete;
        Assert.Empty(fixture.Service.Definitions());
        Assert.Null(fixture.Store.Load(definition.Id));
    }
}
