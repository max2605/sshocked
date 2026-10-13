using System.Globalization;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class LogMonitoringState
{
    public static long? Number(IReadOnlyDictionary<string, string>? state, string key) => state is not null && state.TryGetValue(key, out var text)
        && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static LogMonitoringCursor? Cursor(MonitoringRecord? record)
    {
        var state = record?.State;
        if (state is null || !state.TryGetValue("file.identity", out var identity) || Number(state, "file.offset") is not { } offset
            || Number(state, "file.prefix.length") is not { } prefixLength || prefixLength > 256
            || Number(state, "file.anchor.offset") is not { } anchorOffset || Number(state, "file.anchor.length") is not { } anchorLength || anchorLength > 128
            || !state.TryGetValue("file.prefix.hash", out var prefix) || !LogMonitoringParser.Hash(prefix)
            || !state.TryGetValue("file.anchor.hash", out var anchor) || !LogMonitoringParser.Hash(anchor)) return null;
        return new(identity, offset, (int)prefixLength, prefix, anchorOffset, (int)anchorLength, anchor,
            state.GetValueOrDefault("file.skip") == "true", Number(state, "file.epoch") ?? 0);
    }

    public static void SetCursor(Dictionary<string, string> state, LogMonitoringCursor cursor)
    {
        state["file.identity"] = cursor.Identity;
        Set(state, "file.offset", cursor.Offset);
        Set(state, "file.prefix.length", cursor.PrefixLength);
        state["file.prefix.hash"] = cursor.PrefixHash;
        Set(state, "file.anchor.offset", cursor.AnchorOffset);
        Set(state, "file.anchor.length", cursor.AnchorLength);
        state["file.anchor.hash"] = cursor.AnchorHash;
        state["file.skip"] = cursor.SkipUntilLf ? "true" : "false";
        Set(state, "file.epoch", cursor.Epoch);
    }

    public static List<LogMonitoringBucket> Buckets(MonitoringRecord? previous, int maximum, out bool lost)
    {
        lost = false;
        var encoded = previous?.State.GetValueOrDefault("window.buckets");
        if (string.IsNullOrEmpty(encoded)) return [];
        var rows = encoded.Split(';');
        if (rows.Length > maximum) { lost = true; return []; }
        var values = new List<LogMonitoringBucket>();
        foreach (var row in rows)
        {
            var fields = row.Split(':');
            if (fields.Length != 3 || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var observed)
                || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var lines)
                || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var matches) || matches > lines) { lost = true; return []; }
            values.Add(new(observed, lines, matches));
        }
        return values;
    }

    public static string Encode(IEnumerable<LogMonitoringBucket> buckets) => string.Join(';', buckets.Select(bucket =>
        string.Join(':', bucket.ObservedMilliseconds.ToString(CultureInfo.InvariantCulture), bucket.Lines.ToString(CultureInfo.InvariantCulture), bucket.Matches.ToString(CultureInfo.InvariantCulture))));

    public static void Set(Dictionary<string, string> state, string key, long value) => state[key] = value.ToString(CultureInfo.InvariantCulture);
}
