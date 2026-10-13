using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MonitoringProviderQaTests
{
    [Fact]
    public void DiskOpenFailureDoesNotRecoverAnExistingSmartHistoryAlarm()
    {
        var definition = new MonitorDefinition();
        var observation = SmartMonitoringProvider.ParseLinux("""{"smartctl":{"exit_status":2}}""", 2);
        var record = new MonitoringRecord
        { Alarms = [new() { Key = "smart.history", Name = "SMART warning", Severity = MonitorSeverity.Warning, Active = true }] };
        Assert.Null(observation.Conditions.Single(condition => condition.Key == "smart.history").Triggered);
        new MonitorAlertEvaluator().Evaluate(definition, observation, record, DateTimeOffset.UtcNow, 20);
        Assert.True(record.Alarms.Single(alarm => alarm.Key == "smart.history").Active);
        Assert.DoesNotContain(record.Events, entry => entry.Action == "recovered");
    }

    [Fact]
    public void ConfirmedSmartWarningsRemainVisibleEvenWithOtherDiagnosticBits()
    {
        var observation = SmartMonitoringProvider.ParseLinux("""{"smartctl":{"exit_status":18}}""", 18);
        Assert.True(observation.Conditions.Single(condition => condition.Key == "smart.history").Triggered);
        Assert.Equal(MonitorDataQuality.Partial, observation.Snapshot.Quality);
    }

    [Fact]
    public async Task DisposingMonitoringCancelsAnActiveRunOneOperation()
    {
        using var fixture = new MonitoringTestFixture();
        var definition = fixture.Add();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Work = async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return MonitoringTestProvider.Value(1);
        };
        var operation = fixture.Service.RunOne(definition.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Service.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
