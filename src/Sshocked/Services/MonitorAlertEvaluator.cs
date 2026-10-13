using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MonitorAlertEvaluator
{
    public void Evaluate(MonitorDefinition definition, MonitoringObservation observation,
        MonitoringRecord record, DateTimeOffset now, int maximumEvents)
    {
        var conditions = observation.Conditions.ToList();
        foreach (var rule in definition.Rules)
        {
            if (conditions.Any(condition => condition.Key == rule.Id)) continue;
            var metric = observation.Snapshot.Metrics.FirstOrDefault(item => item.Key == rule.MetricKey);
            var previous = record.Alarms.FirstOrDefault(item => item.Key == rule.Id);
            var threshold = previous?.Active == true ? rule.ClearThreshold ?? rule.Threshold : rule.Threshold;
            bool? triggered = metric?.Quality == MonitorDataQuality.Success && metric.NumericValue is { } value && double.IsFinite(value)
                ? MonitoringValidation.Compare(value, rule.Comparison, threshold) : null;
            conditions.Add(new(rule.Id, rule.Name, triggered, rule.Severity, null, rule.ConsecutiveSamples, rule.HoldSeconds));
        }
        var uncertain = observation.Snapshot.Quality is not MonitorDataQuality.Success;
        if (observation.Snapshot.Quality is not (MonitorDataQuality.Success or MonitorDataQuality.Partial or MonitorDataQuality.Baseline))
            foreach (var state in record.Alarms) { state.ConsecutiveHits = 0; state.ConditionSinceUtc = null; }
        foreach (var condition in conditions)
        {
            var alarm = record.Alarms.FirstOrDefault(item => item.Key == condition.Key);
            if (alarm is null)
            {
                alarm = new() { Key = condition.Key, Name = condition.Name, Severity = condition.Severity };
                record.Alarms.Add(alarm);
            }
            alarm.Message = condition.Message;
            if (condition.Triggered is null)
            { alarm.ConsecutiveHits = 0; alarm.ConditionSinceUtc = null; uncertain = true; continue; }
            if (condition.Triggered == true)
            {
                if (alarm.ConditionSinceUtc > now) { alarm.ConditionSinceUtc = null; alarm.ConsecutiveHits = 0; }
                alarm.ConditionSinceUtc ??= now;
                alarm.ConsecutiveHits = Math.Min(10000, alarm.ConsecutiveHits + 1);
                var count = Math.Max(definition.ConsecutiveSamples, condition.ConsecutiveSamples);
                var hold = Math.Max(definition.GraceSeconds, condition.HoldSeconds);
                if (!alarm.Active && alarm.ConsecutiveHits >= count && now - alarm.ConditionSinceUtc.Value >= TimeSpan.FromSeconds(hold))
                {
                    alarm.Active = true;
                    alarm.Acknowledged = false;
                    alarm.RaisedUtc = now;
                    alarm.Severity = condition.Severity;
                    record.Events.Add(new(now, "raised", alarm.Key, alarm.Severity));
                }
                else if (alarm.Active && alarm.Severity != condition.Severity)
                {
                    alarm.Severity = condition.Severity;
                    alarm.Acknowledged = false;
                    record.Events.Add(new(now, "changed", alarm.Key, alarm.Severity));
                }
            }
            else
            {
                alarm.ConsecutiveHits = 0;
                alarm.ConditionSinceUtc = null;
                if (alarm.Active)
                {
                    alarm.Active = false;
                    alarm.Acknowledged = false;
                    record.Events.Add(new(now, "recovered", alarm.Key, alarm.Severity));
                }
            }
        }
        var active = record.Alarms.Where(alarm => alarm.Active).Select(alarm => alarm.Severity).ToList();
        observation.Snapshot.Severity = active.Count > 0 ? active.Max() : uncertain ? MonitorSeverity.Unknown : MonitorSeverity.Ok;
        if (record.Events.Count > maximumEvents) record.Events.RemoveRange(0, record.Events.Count - maximumEvents);
    }
}
