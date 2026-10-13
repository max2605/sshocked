using System.Text;
using System.Text.RegularExpressions;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class LogMonitoringEvaluator
{
    public static (long Offset, bool Skip, long Lines, long Matches, bool Gap) Count(LogMonitoringRead read, bool skip, LogMonitoringPattern pattern, int maximumLine, CancellationToken ct)
    {
        var start = 0;
        var offset = read.Start;
        var count = 0L;
        var matches = 0L;
        var gap = false;
        var regexFailed = false;
        var encoding = new UTF8Encoding(false, true);
        for (var index = 0; index < read.Bytes.Length; index++)
        {
            if (read.Bytes[index] != 10) continue;
            ct.ThrowIfCancellationRequested();
            var length = index - start;
            if (!skip && length <= maximumLine)
            {
                try
                {
                    var line = encoding.GetString(read.Bytes, start, length);
                    if (line.EndsWith('\r')) line = line[..^1];
                    count++;
                    if (!regexFailed && pattern.Matches(line)) matches++;
                }
                catch (DecoderFallbackException) { gap = true; }
                catch (RegexMatchTimeoutException) { gap = true; regexFailed = true; }
            }
            else gap = true;
            skip = false;
            start = index + 1;
            offset = read.Start + start;
        }
        if (skip || read.Bytes.Length - start > maximumLine)
        {
            if (read.Bytes.Length > start) gap = true;
            skip = true;
            offset = read.Start + read.Bytes.Length;
        }
        return (offset, skip, count, matches, gap);
    }

    public static void ProvenConditions(MonitoringObservation observation, MonitorDefinition definition, MonitoringRecord? previous, long lines, long matches)
    {
        foreach (var rule in definition.Rules)
        {
            var count = rule.MetricKey == "log.lines" ? lines : matches;
            var threshold = previous?.Alarms.FirstOrDefault(alarm => alarm.Key == rule.Id)?.Active == true ? rule.ClearThreshold ?? rule.Threshold : rule.Threshold;
            var proven = rule.Comparison switch
            {
                MonitorComparison.Greater => count > threshold,
                MonitorComparison.GreaterOrEqual => count >= threshold,
                MonitorComparison.NotEqual => count > threshold,
                _ => false
            };
            if (proven) observation.Conditions.Add(new(rule.Id, rule.Name, true, rule.Severity,
                "The observed lower bound already proves this alarm despite incomplete coverage.", rule.ConsecutiveSamples, rule.HoldSeconds));
        }
    }
}
