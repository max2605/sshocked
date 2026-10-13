using System.Globalization;
using Sshocked.Models;

namespace Sshocked.Services;

public static class MonitoringSchedule
{
    public static TimeSpan Delay(MonitorDefinition definition, MonitoringRecord? record)
    {
        var failures = record?.State.TryGetValue(MonitoringStateKeys.Failures, out var text) == true &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? Math.Clamp(count, 0, 10) : 0;
        return TimeSpan.FromSeconds(Math.Min(Math.Max(900, definition.IntervalSeconds),
            definition.IntervalSeconds * Math.Pow(2, failures)));
    }
}
