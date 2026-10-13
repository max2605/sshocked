using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MonitorAlertEvaluatorTests
{
    [Fact]
    public void ProviderCoverageConditionOverridesGenericRuleAndKeepsConsecutiveCounts()
    {
        var rule = new MonitorRule { Id = "count", MetricKey = "log.matches", Threshold = 10, ConsecutiveSamples = 2 };
        var definition = new MonitorDefinition { Rules = [rule] };
        var record = new MonitoringRecord();
        var evaluator = new MonitorAlertEvaluator();
        var now = DateTimeOffset.UtcNow;
        MonitoringObservation Value() => new()
        {
            Snapshot = new() { Quality = MonitorDataQuality.Partial },
            Conditions = [new("count", "proven lower bound", true, ConsecutiveSamples: 2)]
        };
        evaluator.Evaluate(definition, Value(), record, now, 500);
        Assert.False(record.Alarms[0].Active);
        evaluator.Evaluate(definition, Value(), record, now.AddSeconds(1), 500);
        Assert.True(record.Alarms[0].Active);
        Assert.Single(record.Events);
    }

    [Fact]
    public void UnknownSampleBreaksPendingConsecutiveSequenceWithoutRecoveringAnActiveAlarm()
    {
        var definition = new MonitorDefinition();
        var record = new MonitoringRecord();
        var evaluator = new MonitorAlertEvaluator();
        var now = DateTimeOffset.UtcNow;
        MonitoringObservation Value(bool? value) => new()
        {
            Snapshot = new() { Quality = value is null ? MonitorDataQuality.Partial : MonitorDataQuality.Success },
            Conditions = [new("cpu", "CPU", value, ConsecutiveSamples: 2)]
        };
        evaluator.Evaluate(definition, Value(true), record, now, 500);
        evaluator.Evaluate(definition, Value(null), record, now.AddSeconds(1), 500);
        evaluator.Evaluate(definition, Value(true), record, now.AddSeconds(2), 500);
        Assert.False(record.Alarms[0].Active);
        evaluator.Evaluate(definition, Value(true), record, now.AddSeconds(3), 500);
        Assert.True(record.Alarms[0].Active);
        evaluator.Evaluate(definition, Value(null), record, now.AddSeconds(4), 500);
        Assert.True(record.Alarms[0].Active);
        Assert.DoesNotContain(record.Events, item => item.Action == "recovered");
    }

    [Fact]
    public void HoldAndHysteresisPreventPrematureRaiseAndRecovery()
    {
        var definition = new MonitorDefinition { Rules = [new() { Id = "cpu", MetricKey = "cpu.percent", Threshold = 80, ClearThreshold = 70, HoldSeconds = 10 }] };
        var record = new MonitoringRecord();
        var evaluator = new MonitorAlertEvaluator();
        var now = DateTimeOffset.UtcNow;
        evaluator.Evaluate(definition, MonitoringTestProvider.Value(90), record, now, 500);
        Assert.False(record.Alarms[0].Active);
        evaluator.Evaluate(definition, MonitoringTestProvider.Value(90), record, now.AddSeconds(10), 500);
        Assert.True(record.Alarms[0].Active);
        evaluator.Evaluate(definition, MonitoringTestProvider.Value(75), record, now.AddSeconds(11), 500);
        Assert.True(record.Alarms[0].Active);
        evaluator.Evaluate(definition, MonitoringTestProvider.Value(65), record, now.AddSeconds(12), 500);
        Assert.False(record.Alarms[0].Active);
    }
}
