using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class LogMonitoringTests
{
    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public async Task IncompleteWindowOnlyRaisesAnAlreadyProvenGreaterThanTenAlarm(int lines, bool alarm)
    {
        var text = string.Concat(Enumerable.Repeat("ERROR ERROR\r\n", lines));
        var definition = Definition();
        definition.Options["patternMode"] = "regex";
        definition.Options["pattern"] = "ERROR";
        var observation = await Provider(Frame(text), new()).Collect(new(), definition, null);
        Assert.Equal(lines, Metric(observation, "log.matches").NumericValue);
        Assert.Equal(MonitorDataQuality.Partial, observation.Snapshot.Quality);
        Assert.Equal(alarm, observation.Conditions.Any(condition => condition.Key == "alarm" && condition.Triggered == true));
        Assert.DoesNotContain("ERROR", string.Join('\n', observation.State.Values));
    }

    [Fact]
    public async Task CursorCommitAndExactWindowBoundaryAvoidDuplicateCountsAndPermitRecoveryOnlyAfterCoverage()
    {
        const string text = "ERROR\nERROR\n";
        var clock = new LogMonitoringTestsClock();
        var output = Frame(text);
        var transport = new DockerMonitoringTestsTransport(_ => new(0, output, ""));
        var provider = new LogMonitoringProvider(transport, clock);
        var definition = Definition();
        definition.Options["windowValue"] = "1";
        definition.Options["windowUnit"] = "minutes";
        var first = await provider.Collect(new(), definition, null);
        var previous = new MonitoringRecord { State = first.State, Snapshot = first.Snapshot };
        output = Frame(text, start: Encoding.UTF8.GetByteCount(text), reset: "none");
        clock.Advance(TimeSpan.FromSeconds(30));
        var second = await provider.Collect(new(), definition, previous);
        Assert.Equal(2, Metric(second, "log.lines").NumericValue);
        previous.State = second.State;
        previous.Snapshot = second.Snapshot;
        clock.Advance(TimeSpan.FromSeconds(30));
        var third = await provider.Collect(new(), definition, previous);
        Assert.Equal(0, Metric(third, "log.lines").NumericValue);
        Assert.Equal(MonitorDataQuality.Success, third.Snapshot.Quality);
        Assert.Equal(Encoding.UTF8.GetByteCount(text).ToString(), third.State["file.offset"]);
    }

    [Fact]
    public async Task SplitUtf8AndUnterminatedLinesAreRereadWithoutPersistingRawText()
    {
        const string complete = "Grüße ERROR\n";
        var bytes = Encoding.UTF8.GetBytes(complete);
        var clock = new LogMonitoringTestsClock();
        var output = FrameBytes(bytes[..3], total: bytes.Length, reset: "initial");
        var provider = new LogMonitoringProvider(new DockerMonitoringTestsTransport(_ => new(0, output, "")), clock);
        var definition = Definition();
        var first = await provider.Collect(new(), definition, null);
        Assert.Equal("0", first.State["file.offset"]);
        Assert.Equal(0, Metric(first, "log.matches").NumericValue);
        output = Frame(complete, reset: "none");
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await provider.Collect(new(), definition, new() { State = first.State });
        Assert.Equal(1, Metric(second, "log.matches").NumericValue);
        Assert.DoesNotContain("Grüße", string.Join('\n', second.State.Values));
    }

    [Fact]
    public async Task FromNowSkipsExistingPartialLineUntilFirstLf()
    {
        var clock = new LogMonitoringTestsClock();
        var definition = Definition();
        definition.Options["startMode"] = "fromNow";
        var output = Frame("old", start: 3);
        var provider = new LogMonitoringProvider(new DockerMonitoringTestsTransport(_ => new(0, output, "")), clock);
        var first = await provider.Collect(new(), definition, null);
        Assert.Equal("true", first.State["file.skip"]);
        output = Frame("old suffix\nERROR\n", start: 3, reset: "none");
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await provider.Collect(new(), definition, new() { State = first.State });
        Assert.Equal(1, Metric(second, "log.lines").NumericValue);
        Assert.Equal(1, Metric(second, "log.matches").NumericValue);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("truncate")]
    [InlineData("anchor")]
    public async Task RotationAndFastRegrowthResetEpochAndCoverage(string reset)
    {
        var definition = Definition();
        var clock = new LogMonitoringTestsClock();
        var output = Frame("ERROR\n");
        var provider = new LogMonitoringProvider(new DockerMonitoringTestsTransport(_ => new(0, output, "")), clock);
        var first = await provider.Collect(new(), definition, null);
        output = Frame("new\n", reset: reset);
        clock.Advance(TimeSpan.FromMinutes(10));
        var second = await provider.Collect(new(), definition, new() { State = first.State });
        Assert.Equal("2", second.State["file.epoch"]);
        Assert.Equal(MonitorDataQuality.Partial, second.Snapshot.Quality);
        Assert.Contains("Coverage restarted", second.Snapshot.Message);
    }

    [Fact]
    public async Task RestartAndWallClockJumpCannotPretendCompleteCoverage()
    {
        var definition = Definition();
        var clock = new LogMonitoringTestsClock();
        var output = Frame("ERROR\n");
        var transport = new DockerMonitoringTestsTransport(_ => new(0, output, ""));
        var provider = new LogMonitoringProvider(transport, clock);
        var first = await provider.Collect(new(), definition, null);
        output = Frame("ERROR\n", start: 6, reset: "none");
        clock.Advance(TimeSpan.FromMinutes(30));
        var restarted = await new LogMonitoringProvider(transport, clock).Collect(new(), definition, new() { State = first.State });
        Assert.Equal(MonitorDataQuality.Partial, restarted.Snapshot.Quality);
        clock.JumpUtc(TimeSpan.FromHours(-1));
        var jumped = await provider.Collect(new(), definition, new() { State = first.State });
        Assert.Equal(MonitorDataQuality.Partial, jumped.Snapshot.Quality);
    }

    [Fact]
    public async Task MissingFileIsAnErrorAndCannotBecomeZeroMatches()
    {
        var provider = new LogMonitoringProvider(new DockerMonitoringTestsTransport(_ => new(44, "", "LOG_MISSING")), new LogMonitoringTestsClock());
        var error = await Assert.ThrowsAsync<MonitoringProbeException>(() => provider.Collect(new(), Definition(), null));
        Assert.Equal(MonitorDataQuality.Error, error.Quality);
    }

    [Fact]
    public async Task OversizedLineProducesGapAndPatternRemainsOutsideRemoteCommand()
    {
        var definition = Definition();
        definition.Options["maxLineBytes"] = "2";
        definition.Options["patternMode"] = "literal";
        definition.Options["pattern"] = "$(touch sentinel)";
        var transport = new DockerMonitoringTestsTransport(_ => new(0, Frame("long line\n"), ""));
        var observation = await new LogMonitoringProvider(transport, new LogMonitoringTestsClock()).Collect(new(), definition, null);
        Assert.Equal(MonitorDataQuality.Partial, observation.Snapshot.Quality);
        Assert.Contains("Oversized", observation.Snapshot.Message);
        Assert.DoesNotContain("$(touch sentinel)", Assert.Single(transport.Calls).Command);
    }

    [Fact]
    public void LocalPatternTestCountsOneHitPerLineAndParsesAllWindowUnits()
    {
        var definition = Definition();
        definition.Options["patternMode"] = "regex";
        definition.Options["pattern"] = "ERROR";
        Assert.Equal(2, LogMonitoringProvider.TestPattern(definition, ["ERROR ERROR", "ok", "ERROR"]));
        Assert.Equal(["ERROR ERROR", "ERROR"], LogMonitoringProvider.TestPatternLines(definition, ["ERROR ERROR", "ok", "ERROR\r"]));
        foreach (var (unit, expected) in new[] { ("seconds", 1000L), ("minutes", 60000L), ("hours", 3600000L), ("days", 86400000L) })
        {
            definition.Options["windowValue"] = "1";
            definition.Options["windowUnit"] = unit;
            Assert.Equal(expected, LogMonitoringProvider.WindowMilliseconds(definition));
        }
    }

    [Fact]
    public void LineLimitMustLeaveSpaceForLfToAvoidAStuckCursorAtTheReadBoundary()
    {
        var definition = Definition();
        definition.Options["maxReadBytes"] = "1024";
        definition.Options["maxLineBytes"] = "1024";
        Assert.Throws<ArgumentException>(() => Provider("", new()).Validate(definition));
        definition.Options["maxLineBytes"] = "1023";
        Provider("", new()).Validate(definition);
    }

    [Fact]
    public void ProbeParserRejectsMixedGenerationPayloadAndLengthMismatch()
    {
        var valid = Frame("ERROR\n");
        Assert.Throws<InvalidDataException>(() => LogMonitoringParser.Parse(valid.Replace("SSKLOGEND 1:2", "SSKLOGEND 1:3", StringComparison.Ordinal), 32768));
        Assert.Throws<InvalidDataException>(() => LogMonitoringParser.Parse(valid.Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes("ERROR\n")), "", StringComparison.Ordinal), 32768));
    }

    [Theory]
    [InlineData(MonitorComparison.Greater, false)]
    [InlineData(MonitorComparison.GreaterOrEqual, true)]
    [InlineData(MonitorComparison.Less, false)]
    [InlineData(MonitorComparison.LessOrEqual, false)]
    [InlineData(MonitorComparison.Equal, false)]
    [InlineData(MonitorComparison.NotEqual, false)]
    public async Task IncompleteLowerBoundDoesNotProveAbsenceOrEquality(MonitorComparison comparison, bool proven)
    {
        var definition = Definition();
        definition.Rules[0].Comparison = comparison;
        definition.Rules[0].Threshold = 10;
        var observation = await Provider(Frame(string.Concat(Enumerable.Repeat("entry\n", 10))), new()).Collect(new(), definition, null);
        Assert.Equal(proven, observation.Conditions.Any(condition => condition.Triggered == true));
    }

    [Fact]
    public async Task IncompleteWindowNeverClearsAnActiveAlarmThroughTheSharedEvaluator()
    {
        var definition = Definition();
        var observation = await Provider(Frame("entry\n"), new()).Collect(new(), definition, null);
        var previous = new MonitoringRecord { Alarms = [new() { Key = "alarm", Name = "Errors", Active = true, Severity = MonitorSeverity.Warning }] };
        new MonitorAlertEvaluator().Evaluate(definition, observation, previous, DateTimeOffset.UtcNow, 100);
        Assert.True(Assert.Single(previous.Alarms).Active);
        Assert.DoesNotContain(previous.Events, item => item.Action == "recovered");
    }

    [Fact]
    public async Task RegexTimeoutAndBucketOverflowAreExplicitCoverageGaps()
    {
        var definition = Definition();
        definition.Options["patternMode"] = "regex";
        definition.Options["pattern"] = "^(a+)+$";
        definition.Options["regexTimeoutMs"] = "10";
        var observation = await Provider(Frame(new string('a', 8000) + "!\n"), new()).Collect(new(), definition, null);
        Assert.Equal(MonitorDataQuality.Partial, observation.Snapshot.Quality);
        Assert.Contains("regex timeout", observation.Snapshot.Message);
        Assert.DoesNotContain(observation.Conditions, condition => condition.Triggered == true);
        definition = Definition();
        definition.Options["maxBuckets"] = "1";
        var clock = new LogMonitoringTestsClock();
        var output = Frame("a\n");
        var provider = new LogMonitoringProvider(new DockerMonitoringTestsTransport(_ => new(0, output, "")), clock);
        var first = await provider.Collect(new(), definition, null);
        output = Frame("a\nb\n", start: 2, reset: "none");
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await provider.Collect(new(), definition, new() { State = first.State });
        Assert.Equal(MonitorDataQuality.Partial, second.Snapshot.Quality);
        Assert.Single(second.State["window.buckets"].Split(';'));
    }

    [Fact]
    public async Task GeneratedProbeHasValidPosixSyntaxAndQuotesAPathWithShellOperators()
    {
        var bash = OperatingSystem.IsWindows() ? @"C:\Program Files\Git\bin\bash.exe" : "/bin/sh";
        if (!File.Exists(bash)) return;
        var source = "set -eu\n" + LogMonitoringCommands.Source("/var/log/name'; touch sentinel; #", null, true, 1024);
        var info = new ProcessStartInfo(bash) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add("-n"); info.ArgumentList.Add("-c"); info.ArgumentList.Add(source);
        var result = await new ExternalProcessRunner().Run(info);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("path='/var/log/name'\"'\"'; touch sentinel; #'", source);
    }

    private static MonitorDefinition Definition() => new()
    {
        Provider = "log",
        Options = new() { ["path"] = "/var/log/app.log", ["startMode"] = "fromBeginning" },
        Rules = [new() { Id = "alarm", Name = "Errors", MetricKey = "log.matches", Threshold = 10, Comparison = MonitorComparison.Greater }]
    };
    private static LogMonitoringProvider Provider(string output, LogMonitoringTestsClock clock) => new(new DockerMonitoringTestsTransport(_ => new(0, output, "")), clock);
    private static MonitorMetric Metric(MonitoringObservation observation, string key) => Assert.Single(observation.Snapshot.Metrics, metric => metric.Key == key);
    private static string Frame(string text, int start = 0, string reset = "initial") => FrameBytes(Encoding.UTF8.GetBytes(text)[start..], Encoding.UTF8.GetByteCount(text), start, reset, text.EndsWith('\n'));
    private static string FrameBytes(byte[] payload, int? total = null, int start = 0, string reset = "initial", bool endsLf = true)
    {
        var size = total ?? payload.Length;
        var hash = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();
        return $"SSKLOG1 1:2 {size} {start} {payload.Length} 0 {hash} 0 0 {hash} {reset} {(endsLf ? 1 : 0)}\n{Convert.ToBase64String(payload)}\nSSKLOGEND 1:2 {size} {hash} {hash}\n";
    }
}
