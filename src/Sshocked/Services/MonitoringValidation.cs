using Sshocked.Models;

namespace Sshocked.Services;

public static class MonitoringValidation
{
    public static void Validate(MonitorDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 80 || definition.Id.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 80 || definition.Name.Any(char.IsControl))
            throw new ArgumentException("A monitor needs an ID and a name of 1 to 80 characters.");
        if (definition.IntervalSeconds is < 1 or > 604800 || definition.TimeoutSeconds is < 1 or > 3600 ||
            definition.MaxOutputBytes is < 1024 or > 16777216 || definition.FreshnessSeconds is < 1 or > 1209600 ||
            definition.GraceSeconds is < 0 or > 604800 || definition.ConsecutiveSamples is < 1 or > 10000)
            throw new ArgumentException("Monitor intervals, timeout, output limit or alarm delay are outside supported bounds.");
        if (definition.Options.Count > 64 || definition.Options.Any(pair => pair.Key.Length > 80 ||
            pair.Value.Length > 16384 || pair.Key.Any(char.IsControl) || pair.Value.Contains('\0')))
            throw new ArgumentException("Monitor options exceed their limits.");
        if (definition.Rules.Count > 64 || definition.Rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != definition.Rules.Count)
            throw new ArgumentException("A monitor supports at most 64 uniquely identified rules.");
        foreach (var rule in definition.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || rule.Id.Length > 80 || rule.Id.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(rule.MetricKey) || rule.MetricKey.Length > 160 || rule.MetricKey.Any(char.IsControl) ||
                rule.Name.Length > 160 || rule.Name.Any(char.IsControl) ||
                !double.IsFinite(rule.Threshold) || rule.ClearThreshold is { } clear && !double.IsFinite(clear) ||
                rule.Severity is not (MonitorSeverity.Warning or MonitorSeverity.Critical) ||
                !Enum.IsDefined(rule.Comparison) || rule.ConsecutiveSamples is < 1 or > 10000 || rule.HoldSeconds is < 0 or > 604800)
                throw new ArgumentException("Invalid monitor alarm rule.");
        }
    }

    public static bool Compare(double value, MonitorComparison comparison, double threshold) => comparison switch
    {
        MonitorComparison.Greater => value > threshold,
        MonitorComparison.GreaterOrEqual => value >= threshold,
        MonitorComparison.Less => value < threshold,
        MonitorComparison.LessOrEqual => value <= threshold,
        MonitorComparison.Equal => value == threshold,
        MonitorComparison.NotEqual => value != threshold,
        _ => throw new ArgumentException("Unknown comparison.")
    };
}
