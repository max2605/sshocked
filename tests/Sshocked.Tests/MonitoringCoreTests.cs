using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MonitoringCoreTests
{
    [Fact]
    public async Task FailedProbePreservesAnActiveAlarmAndThePreviousSample()
    {
        using var fixture = new MonitoringTestFixture();
        var definition = fixture.Add();
        var original = fixture.Service.Find(definition.Id);
        original.Rules.Add(new() { MetricKey = "cpu.percent", Threshold = 70, Severity = MonitorSeverity.Critical });
        fixture.Service.Save(original, definition);
        var first = Assert.Single(await fixture.Service.Run());
        Assert.Equal(MonitorSeverity.Critical, first.Severity);
        var sampled = first.Metrics[0].SampledUtc;
        fixture.Clock.Advance(TimeSpan.FromSeconds(60));
        fixture.Provider.Work = (_, _) => throw new MonitoringProbeException(MonitorDataQuality.AuthenticationRequired, "Access denied");
        var failed = Assert.Single(await fixture.Service.Run());
        Assert.Equal(MonitorDataQuality.AuthenticationRequired, failed.Quality);
        Assert.Equal(MonitorSeverity.Critical, failed.Severity);
        Assert.Equal(80, failed.Metrics[0].NumericValue);
        Assert.Equal(MonitorDataQuality.Stale, failed.Metrics[0].Quality);
        Assert.Equal(sampled, failed.Metrics[0].SampledUtc);
        Assert.DoesNotContain(fixture.Store.Load(definition.Id)!.Events, entry => entry.Action == "recovered");
    }

    [Fact]
    public async Task PartialMissingValueKeepsItsOldSampleWithoutEvaluatingItAsFresh()
    {
        using var fixture = new MonitoringTestFixture();
        fixture.Add();
        await fixture.Service.Run();
        fixture.Provider.Work = (_, _) => Task.FromResult(MonitoringTestProvider.Value(null, MonitorDataQuality.Partial));
        var result = Assert.Single(await fixture.Service.Run());
        Assert.Equal(80, result.Metrics[0].NumericValue);
        Assert.Equal(MonitorDataQuality.Stale, result.Metrics[0].Quality);
        Assert.Equal(MonitorSeverity.Unknown, result.Severity);
    }

    [Fact]
    public async Task CollectionIsBoundedAndCancellationStartsNoMoreProbes()
    {
        using var fixture = new MonitoringTestFixture();
        for (var i = 0; i < 6; i++)
        {
            var id = "host-" + i;
            fixture.Repository.Update(config => config.Hosts.Add(new() { Id = id, HostName = id + ".invalid", User = "user" }));
            fixture.Add("server-" + i, id);
        }
        using var cancellation = new CancellationTokenSource();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Work = async (_, ct) =>
        {
            if (fixture.Provider.MaximumActive == 2) reached.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return MonitoringTestProvider.Value(1);
        };
        var run = fixture.Service.Run(cancellationToken: cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(2, fixture.Provider.MaximumActive);
        Assert.Equal(2, fixture.Provider.Calls);
    }

    [Fact]
    public async Task AlarmAuditStillWorksWhenEventRetentionIsFull()
    {
        using var fixture = new MonitoringTestFixture();
        var definition = fixture.Add();
        fixture.Repository.Update(config => { config.Monitoring.MaxHistoryEntries = 2; config.Monitoring.Monitors[0].Rules.Add(new() { MetricKey = "cpu.percent", Threshold = 70 }); });
        for (var index = 0; index < 8; index++)
        {
            var value = index % 2 == 0 ? 80 : 10;
            fixture.Provider.Work = (_, _) => Task.FromResult(MonitoringTestProvider.Value(value));
            await fixture.Service.Run();
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(2, fixture.Store.Load(definition.Id)!.Events.Count);
        Assert.Equal(8, fixture.Audit.Events.Count(entry => entry.EventType is "monitor.raised" or "monitor.recovered"));
    }

    [Fact]
    public async Task AcknowledgementDoesNotChangeMeasuredAlarmAndStatusBecomesStale()
    {
        using var fixture = new MonitoringTestFixture();
        var definition = fixture.Add();
        fixture.Repository.Update(config => config.Monitoring.Monitors[0].Rules.Add(new() { Id = "cpu", MetricKey = "cpu.percent", Threshold = 70 }));
        await fixture.Service.Run();
        await fixture.Service.Acknowledge(definition.Id, "cpu");
        Assert.True(fixture.Store.Load(definition.Id)!.Alarms[0].Acknowledged);
        Assert.Equal(MonitorSeverity.Warning, Assert.Single(fixture.Service.Status()).Severity);
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(MonitorDataQuality.Stale, Assert.Single(fixture.Service.Status()).Quality);
    }

    [Fact]
    public void EditingUsesAnExpectedSnapshotAndPreservesOtherSettings()
    {
        using var fixture = new MonitoringTestFixture();
        var definition = fixture.Add();
        var edited = fixture.Service.Find(definition.Id);
        edited.Name = "updated";
        fixture.Repository.Update(config => config.Monitoring.Monitors[0].IntervalSeconds = 90);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.Save(edited, definition));
        Assert.Equal(90, fixture.Service.Find(definition.Id).IntervalSeconds);
        Assert.Equal("web", fixture.Repository.Load().Hosts[0].Alias);
    }

    [Fact]
    public async Task ADisabledQueuedMonitorDoesNotStartButAnExplicitCheckStillWorks()
    {
        using var fixture = new MonitoringTestFixture();
        fixture.Add("first");
        var second = fixture.Add("second");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Work = async (_, ct) =>
        {
            started.TrySetResult();
            await finish.Task.WaitAsync(ct);
            return MonitoringTestProvider.Value(10);
        };
        var run = fixture.Service.Run();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Repository.Update(config => config.Monitoring.Monitors.Single(item => item.Id == second.Id).Enabled = false);
        finish.SetResult();
        var snapshots = await run;
        Assert.Equal(MonitorDataQuality.Disabled, snapshots.Single(item => item.MonitorId == second.Id).Quality);
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.Equal(MonitorDataQuality.Success, (await fixture.Service.RunOne(second.Id)).Quality);
        Assert.Equal(2, fixture.Provider.Calls);
    }
}
