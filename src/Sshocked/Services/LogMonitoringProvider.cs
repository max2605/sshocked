using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class LogMonitoringProvider(IMonitoringTransport transport, TimeProvider clock) : IMonitoringProvider
{
    private readonly string session = Guid.NewGuid().ToString("N");
    private readonly long sessionTimestamp = clock.GetTimestamp();
    private readonly long sessionUtc = clock.GetUtcNow().ToUnixTimeMilliseconds();
    public string Key => "log";
    public string Kind => "log";
    public IReadOnlyList<MonitorOption> Options { get; } = [
        new("platform", "Remote platform", "linux", Choices: ["linux"]),
        new("path", "Absolute Linux log file path", Required: true),
        new("patternMode", "Line selection", "all", Choices: ["all", "literal", "regex"]),
        new("pattern", "Local literal/regex pattern (empty for all lines)"),
        new("ignoreCase", "Ignore case", "false", Choices: ["false", "true"]),
        new("windowValue", "Sliding observation-time window length", "5"),
        new("windowUnit", "Window unit (day = 24 hours)", "minutes", Choices: ["seconds", "minutes", "hours", "days"]),
        new("startMode", "Initial file position", "fromNow", Choices: ["fromNow", "fromBeginning"]),
        new("maxReadBytes", "Bytes read per poll", "32768"),
        new("maxLineBytes", "Maximum complete line bytes", "16384"),
        new("maxBacklogBytes", "Maximum backlog before skipping with a coverage gap", "16777216"),
        new("maxBuckets", "Maximum persisted observation-time buckets", "4096"),
        new("regexTimeoutMs", "Regex timeout in milliseconds", "100"),
        new("allowSudo", "Use configured noninteractive sudo", "false", Choices: ["false", "true"])];

    public void Validate(MonitorDefinition definition)
    {
        if (MonitoringOptions.Get(definition, "platform", "linux") != "linux") throw new NotSupportedException("File monitoring currently supports remote Linux only.");
        var path = MonitoringOptions.Get(definition, "path");
        if (!path.StartsWith('/') || path.Length > 4096 || path.Any(character => character is '\0' or '\r' or '\n')) throw new ArgumentException("Log monitoring requires an absolute Linux file path without NUL or line breaks.");
        var mode = MonitoringOptions.Get(definition, "patternMode", "all");
        if (mode is not ("all" or "literal" or "regex")) throw new ArgumentException("Pattern mode must be all, literal or regex.");
        if (mode != "all" && string.IsNullOrEmpty(MonitoringOptions.Get(definition, "pattern"))) throw new ArgumentException("Literal and regex modes require a pattern.");
        if (MonitoringOptions.Get(definition, "startMode", "fromNow") is not ("fromNow" or "fromBeginning")) throw new ArgumentException("Start mode must be fromNow or fromBeginning.");
        var bytes = MonitoringOptions.Integer(definition, "maxReadBytes", 32768, 1024, 262144);
        if (MonitoringOptions.Integer(definition, "maxLineBytes", 16384, 1, 65536) >= bytes) throw new ArgumentException("The maximum line length must be smaller than bytes read per poll, leaving space for LF.");
        if ((long)bytes * 4 / 3 + 4096 > definition.MaxOutputBytes) throw new ArgumentException("Monitoring output cap must allow base64 framing of the configured read size.");
        MonitoringOptions.Integer(definition, "maxBacklogBytes", 16777216, bytes, 67108864);
        MonitoringOptions.Integer(definition, "maxBuckets", 4096, 1, 10000);
        MonitoringOptions.Integer(definition, "regexTimeoutMs", 100, 10, 500);
        MonitoringOptions.Boolean(definition, "allowSudo");
        MonitoringOptions.Boolean(definition, "ignoreCase");
        WindowMilliseconds(definition);
        _ = new LogMonitoringPattern(definition);
        foreach (var rule in definition.Rules)
            if (rule.MetricKey is not ("log.lines" or "log.matches") || rule.Threshold < 0 || rule.Threshold != Math.Truncate(rule.Threshold)
                || rule.ClearThreshold is { } clear && (clear < 0 || clear != Math.Truncate(clear)))
                throw new ArgumentException("Log rules must compare log.lines or log.matches against nonnegative whole-number counts.");
    }

    public static int TestPattern(MonitorDefinition definition, IEnumerable<string> lines)
        => TestPatternLines(definition, lines).Count;

    public static IReadOnlyList<string> TestPatternLines(MonitorDefinition definition, IEnumerable<string> lines)
    {
        var pattern = new LogMonitoringPattern(definition);
        var maximum = MonitoringOptions.Integer(definition, "maxLineBytes", 16384, 1, 65536);
        return lines.Take(1000).Select(line =>
        {
            if (Encoding.UTF8.GetByteCount(line) > maximum) throw new ArgumentException("A pattern-test sample exceeds the configured line limit.");
            return line.EndsWith('\r') ? line[..^1] : line;
        }).Where(pattern.Matches).ToArray();
    }

    public async Task<MonitoringObservation> Collect(ServerHost host, MonitorDefinition definition, MonitoringRecord? previous, CancellationToken cancellationToken = default)
    {
        Validate(definition);
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', host.Id, host.HostName, host.User,
            host.Port.ToString(CultureInfo.InvariantCulture), host.ProxyJump, host.AzureBastion?.TargetResourceId, host.AzureBastion?.TargetIpAddress, definition.Id,
            MonitoringOptions.Get(definition, "path"), MonitoringOptions.Get(definition, "patternMode", "all"), MonitoringOptions.Get(definition, "pattern"),
            MonitoringOptions.Get(definition, "ignoreCase", "false"), WindowMilliseconds(definition).ToString(CultureInfo.InvariantCulture), MonitoringOptions.Get(definition, "allowSudo", "false")))));
        var changed = previous?.State.GetValueOrDefault("definition.signature") != signature;
        var cursor = changed ? null : LogMonitoringState.Cursor(previous);
        var bytes = MonitoringOptions.Integer(definition, "maxReadBytes", 32768, 1024, 262144);
        var started = clock.GetTimestamp();
        var result = await transport.Execute(host, LogMonitoringCommands.Read(MonitoringOptions.Get(definition, "path"), cursor,
            MonitoringOptions.Get(definition, "startMode", "fromNow") == "fromNow", bytes), MonitoringProbeResult.Options(definition, MonitoringOptions.Boolean(definition, "allowSudo")), cancellationToken);
        if (result.ExitCode == 44) throw new MonitoringProbeException(MonitorDataQuality.Error, "The monitored log file is missing. No zero count or recovery is inferred.");
        if (result.ExitCode == 23) throw new MonitoringProbeException(MonitorDataQuality.PermissionDenied, "The monitored log file is not readable by the configured account.");
        MonitoringProbeResult.Ensure(result);
        var read = LogMonitoringParser.Parse(result.StandardOutput, bytes);
        if (read.Reset == "none" && (cursor is null || read.Start != cursor.Offset))
            throw new InvalidDataException("Log probe did not resume the committed cursor.");
        var now = clock.GetUtcNow();
        var timestamp = clock.GetTimestamp();
        var sameSession = !changed && previous?.State.GetValueOrDefault("clock.session") == session;
        var oldTimestamp = LogMonitoringState.Number(previous?.State, "clock.timestamp");
        var elapsed = sameSession && oldTimestamp is { } prior ? clock.GetElapsedTime(prior, timestamp).TotalMilliseconds : 0;
        var oldUtc = LogMonitoringState.Number(previous?.State, "clock.utc");
        var utcElapsed = oldUtc is { } old ? now.ToUnixTimeMilliseconds() - old : 0;
        var clockGap = !sameSession || elapsed < 0 || Math.Abs(utcElapsed - elapsed) > 2000;
        var logical = sessionUtc + (long)clock.GetElapsedTime(sessionTimestamp, timestamp).TotalMilliseconds;
        var gap = changed || cursor is null || clockGap || previous?.State.ContainsKey("core.failures") == true
            || elapsed > Math.Max(definition.FreshnessSeconds, definition.IntervalSeconds * 2d) * 1000 || read.Reset is "identity" or "truncate" or "anchor";
        var skip = read.Reset == "initial" ? read.Start == read.Size && !read.EndsWithLf : read.Reset == "none" && cursor?.SkipUntilLf == true;
        var counted = LogMonitoringEvaluator.Count(read, skip, new LogMonitoringPattern(definition), MonitoringOptions.Integer(definition, "maxLineBytes", 16384, 1, 65536), cancellationToken);
        var readBacklog = read.Start + read.Bytes.Length < read.Size;
        gap |= counted.Gap || readBacklog;
        var offset = counted.Offset;
        var backlogExceeded = read.Size - read.Start > MonitoringOptions.Integer(definition, "maxBacklogBytes", 16777216, bytes, 67108864);
        if (backlogExceeded) { offset = read.Size; skip = !read.EndsWithLf; gap = true; }
        else skip = counted.Skip;
        var window = WindowMilliseconds(definition);
        var maximumBuckets = MonitoringOptions.Integer(definition, "maxBuckets", 4096, 1, 10000);
        var lostBuckets = false;
        var buckets = changed || clockGap ? [] : LogMonitoringState.Buckets(previous, maximumBuckets, out lostBuckets);
        gap |= lostBuckets;
        buckets = buckets.Where(bucket => bucket.ObservedMilliseconds > logical - window && bucket.ObservedMilliseconds <= logical).ToList();
        if (counted.Lines > 0) buckets.Add(new(logical, counted.Lines, counted.Matches));
        var bucketOverflow = buckets.Count > maximumBuckets;
        if (bucketOverflow) { buckets.RemoveRange(0, buckets.Count - maximumBuckets); gap = true; }
        var coverageStart = gap ? logical : LogMonitoringState.Number(previous?.State, "window.coverageStart") ?? logical;
        var complete = logical - coverageStart >= window;
        var lines = buckets.Sum(bucket => bucket.Lines);
        var matches = buckets.Sum(bucket => bucket.Matches);
        var observation = new MonitoringObservation
        {
            Snapshot = new()
            {
                Quality = complete ? MonitorDataQuality.Success : MonitorDataQuality.Partial,
                Message = backlogExceeded ? "Backlog limit exceeded; unread bytes were skipped and coverage reset." : counted.Gap ? "Oversized/invalid line or regex timeout caused a coverage gap."
                : bucketOverflow || lostBuckets ? "Observation bucket limit or invalid bucket state caused dropped counts and a coverage gap."
                : readBacklog ? "Bounded read backlog remains; window counts are observed lower bounds."
                : gap ? "Coverage restarted after startup, rotation, delayed poll or interrupted access." : complete ? "Complete observation-time window." : "Window coverage is incomplete; counts are observed lower bounds."
            }
        };
        if (definition.IntervalSeconds * 1000L > window || clock.GetElapsedTime(started, timestamp).TotalMilliseconds > window)
            observation.Snapshot.Message += " Poll/read duration exceeds the window; write-time precision is unavailable.";
        observation.Snapshot.Metrics.Add(new() { Key = "log.lines", Name = "Observed complete lines in sliding window", Unit = "count", NumericValue = lines, Quality = complete ? MonitorDataQuality.Success : MonitorDataQuality.Partial });
        observation.Snapshot.Metrics.Add(new() { Key = "log.matches", Name = "Observed matching lines in sliding window", Unit = "count", NumericValue = matches, Quality = complete ? MonitorDataQuality.Success : MonitorDataQuality.Partial });
        observation.Snapshot.Metrics.Add(new() { Key = "log.coverage", Name = "Window coverage", TextValue = complete ? "complete" : "incomplete" });
        observation.Snapshot.Metrics.Add(new() { Key = "log.cursor", Name = "Committed next byte offset", Unit = "bytes", NumericValue = offset });
        if (!complete) LogMonitoringEvaluator.ProvenConditions(observation, definition, previous, lines, matches);
        var state = observation.State;
        state["definition.signature"] = signature;
        state["clock.session"] = session;
        LogMonitoringState.Set(state, "clock.timestamp", timestamp);
        LogMonitoringState.Set(state, "clock.utc", now.ToUnixTimeMilliseconds());
        LogMonitoringState.Set(state, "clock.logical", logical);
        LogMonitoringState.Set(state, "window.coverageStart", coverageStart);
        state["window.buckets"] = LogMonitoringState.Encode(buckets);
        var epoch = (cursor?.Epoch ?? 0) + (read.Reset == "none" ? 0 : 1);
        LogMonitoringState.SetCursor(state, new(read.Identity, offset, read.PrefixLength, read.PrefixHash, read.AnchorOffset, read.AnchorLength, read.AnchorHash, skip, epoch));
        return observation;
    }

    internal static long WindowMilliseconds(MonitorDefinition definition)
    {
        var value = MonitoringOptions.Integer(definition, "windowValue", 5, 1, 2592000);
        var factor = MonitoringOptions.Get(definition, "windowUnit", "minutes") switch
        { "seconds" => 1000L, "minutes" => 60000L, "hours" => 3600000L, "days" => 86400000L, _ => throw new ArgumentException("Window unit must be seconds, minutes, hours or days.") };
        if (value * factor > 2592000000L) throw new ArgumentException("The log window must not exceed 30 days.");
        return value * factor;
    }
}
