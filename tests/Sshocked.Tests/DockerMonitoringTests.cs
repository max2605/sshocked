using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class DockerMonitoringTests
{
    private static readonly string Id = new('a', 64);
    private static readonly string OtherId = new('b', 64);

    [Fact]
    public async Task FirstInspectionBaselinesRestartsAndResourceUnitsPreserveDockerCpuScale()
    {
        var transport = Transport(Inspect(Id, "app", restarts: 4), stats: Stats(Id));
        var observation = await new DockerMonitoringProvider(transport).Collect(new(), Definition(), null);
        Assert.Null(Metric(observation, "restart.increase").NumericValue);
        Assert.Equal(MonitorDataQuality.Baseline, Metric(observation, "restart.increase").Quality);
        Assert.Equal(250.5, Metric(observation, "cpu.percent").NumericValue);
        Assert.Equal(2 * 1024 * 1024, Metric(observation, "memory.bytes").NumericValue);
        Assert.Equal("notConfigured", Metric(observation, "health").TextValue);
        Assert.False(Condition(observation, "target.health").Triggered);
        Assert.All(transport.Calls, call => Assert.False(call.Options.AllowSudo));
        Assert.DoesNotContain(transport.Calls, call => call.Command.Contains(" restart ", StringComparison.Ordinal) || call.Command.Contains(" exec ", StringComparison.Ordinal));
        Assert.DoesNotContain(transport.Calls, call => call.Command.Contains("json .State}}", StringComparison.Ordinal) || call.Command.Contains(".Config.Env", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestartsCompareOnlySameEngineAndContainerIdentityAndResetOnCounterDrop()
    {
        var definition = Definition();
        var transport = Transport(Inspect(Id, "app", restarts: 5));
        var previous = new MonitoringRecord { State = new() { ["engine.id"] = "engine", ["engine.sudo"] = "False", ["restart." + Id] = "3" } };
        var result = await new DockerMonitoringProvider(transport).Collect(new(), definition, previous);
        Assert.Equal(2, Metric(result, "restart.increase").NumericValue);
        Assert.True(Condition(result, "target.restart").Triggered);
        previous.State["engine.id"] = "other-engine";
        result = await new DockerMonitoringProvider(transport).Collect(new(), definition, previous);
        Assert.Null(Metric(result, "restart.increase").NumericValue);
        previous.State["engine.id"] = "engine";
        previous.State["restart." + Id] = "7";
        result = await new DockerMonitoringProvider(transport).Collect(new(), definition, previous);
        Assert.Null(Metric(result, "restart.increase").NumericValue);
    }

    [Fact]
    public async Task PinnedIdDoesNotFollowReplacementNameWhileNameModeDoes()
    {
        var transport = Transport(Inspect(OtherId, "app"), discovery: Discovery(OtherId, "app"));
        var definition = Definition();
        definition.Options["targetMode"] = "id";
        definition.Options["target"] = Id;
        var result = await new DockerMonitoringProvider(transport).Collect(new(), definition, null);
        Assert.Equal("missing", Metric(result, "state").TextValue);
        Assert.True(Condition(result, "target.availability").Triggered);
        definition.Options["targetMode"] = "name";
        definition.Options["target"] = "app";
        result = await new DockerMonitoringProvider(transport).Collect(new(), definition, null);
        Assert.Equal("running", Metric(result, "state").TextValue);
    }

    [Theory]
    [InlineData("healthy", false)]
    [InlineData("unhealthy", true)]
    [InlineData("starting", true)]
    [InlineData(null, true)]
    public async Task RequiredHealthUsesExplicitDockerHealthAndNeverInventsHealthy(string? health, bool triggered)
    {
        var definition = Definition();
        definition.Options["requireHealthy"] = "true";
        var result = await new DockerMonitoringProvider(Transport(Inspect(Id, "app", health: health))).Collect(new(), definition, null);
        Assert.Equal(triggered, Condition(result, "target.health").Triggered);
        Assert.Equal(triggered, Condition(result, "target.availability").Triggered);
    }

    [Theory]
    [InlineData("job", 0, false)]
    [InlineData("job", 2, true)]
    [InlineData("stopped", 137, false)]
    public async Task JobAndStoppedExpectationsDoNotTreatEveryExitedContainerAsFailure(string expected, int code, bool triggered)
    {
        var definition = Definition();
        definition.Options["expected"] = expected;
        var result = await new DockerMonitoringProvider(Transport(Inspect(Id, "app", status: "exited", exit: code))).Collect(new(), definition, null);
        Assert.Equal(triggered, Condition(result, "target.availability").Triggered);
        Assert.Equal(triggered, Condition(result, "target.exitcode").Triggered);
        Assert.Null(Metric(result, "memory.bytes").NumericValue);
    }

    [Fact]
    public async Task ComposeSelectionUsesBothCanonicalLabelsAndCountsHealthyReplicas()
    {
        var discovery = Discovery(Id, "p-web", "p", "web") + "\n" + Discovery(OtherId, "q-web", "q", "web");
        var definition = Definition();
        definition.Options["targetMode"] = "compose";
        definition.Options["project"] = "p";
        definition.Options["service"] = "web";
        definition.Options["minReplicas"] = "2";
        definition.Options["requireHealthy"] = "true";
        var result = await new DockerMonitoringProvider(Transport(Inspect(Id, "p-web", health: "healthy", project: "p", service: "web"), discovery: discovery)).Collect(new(), definition, null);
        Assert.Equal(1, Metric(result, "containers.total").NumericValue);
        Assert.Equal(1, Metric(result, "containers.ready").NumericValue);
        Assert.True(Condition(result, "target.availability").Triggered);
    }

    [Fact]
    public async Task StatsFailureRetainsStateAndDoesNotBecomeZeroResourceUsage()
    {
        var transport = Transport(Inspect(Id, "app"), statsFailure: true);
        var result = await new DockerMonitoringProvider(transport).Collect(new(), Definition(), null);
        Assert.Equal("running", Metric(result, "state").TextValue);
        Assert.Null(Metric(result, "cpu.percent").NumericValue);
        Assert.Equal(MonitorDataQuality.Partial, result.Snapshot.Quality);
        Assert.False(Condition(result, "target.availability").Triggered);
    }

    [Fact]
    public async Task AccessFailureCannotReportMissingAndNeverFallsBackToSudo()
    {
        var transport = new DockerMonitoringTestsTransport(command => command == "uname -s" ? new(0, "Linux\n", "")
            : new(1, "", "permission denied while trying to connect to Docker"));
        var error = await Assert.ThrowsAsync<MonitoringProbeException>(() => new DockerMonitoringProvider(transport).Collect(new(), Definition(), null));
        Assert.Equal(MonitorDataQuality.PermissionDenied, error.Quality);
        Assert.Equal(2, transport.Calls.Count);
        Assert.All(transport.Calls, call => Assert.False(call.Options.AllowSudo));
    }

    [Fact]
    public void DefinitionRejectsShortIdsAndInvalidHealthyStoppedCombination()
    {
        var provider = new DockerMonitoringProvider(Transport(""));
        var definition = Definition();
        definition.Options["targetMode"] = "id";
        definition.Options["target"] = "abc";
        Assert.Throws<ArgumentException>(() => provider.Validate(definition));
        definition.Options["targetMode"] = "name";
        definition.Options["expected"] = "stopped";
        definition.Options["requireHealthy"] = "true";
        Assert.Throws<ArgumentException>(() => provider.Validate(definition));
    }

    [Theory]
    [InlineData("created", true)]
    [InlineData("running", false)]
    [InlineData("paused", true)]
    [InlineData("restarting", true)]
    [InlineData("exited", true)]
    [InlineData("dead", true)]
    [InlineData("removing", true)]
    public async Task DockerLifecycleStatesRemainStructuredAndOomIsAnExplicitCondition(string state, bool unexpected)
    {
        var output = Inspect(Id, "app", status: state).Replace("\"OOMKilled\":false", "\"OOMKilled\":true", StringComparison.Ordinal);
        var observation = await new DockerMonitoringProvider(Transport(output)).Collect(new(), Definition(), null);
        Assert.Equal(state, Metric(observation, "state").TextValue);
        Assert.Equal(unexpected, Condition(observation, "target.state").Triggered);
        Assert.True(Condition(observation, "target.oom").Triggered);
    }

    private static MonitorDefinition Definition() => new() { Provider = "container", Options = new() { ["target"] = "app" } };
    private static MonitorMetric Metric(MonitoringObservation value, string key) => Assert.Single(value.Snapshot.Metrics, metric => metric.Key == key);
    private static MonitorCondition Condition(MonitoringObservation value, string key) => Assert.Single(value.Conditions, condition => condition.Key == key);
    private static string Discovery(string id, string name, string? project = null, string? service = null) =>
        $"{{\"Id\":\"{id}\",\"Name\":\"{name}\",\"Project\":{Json(project)},\"Service\":{Json(service)}}}";
    private static string Inspect(string id, string name, string status = "running", string? health = null, int exit = 0, int restarts = 0, string? project = null, string? service = null) =>
        $"{{\"Id\":\"{id}\",\"Name\":\"/{name}\",\"Status\":\"{status}\",\"Running\":{(status == "running" ? "true" : "false")},\"ExitCode\":{exit},\"OOMKilled\":false,\"RestartCount\":{restarts},\"Health\":{Json(health)},\"MemoryLimit\":0,\"Project\":{Json(project)},\"Service\":{Json(service)}}}";
    private static string Stats(string id) => $"{{\"ID\":\"{id}\",\"CPUPerc\":\"250.5%\",\"MemUsage\":\"2MiB / 10MB\",\"MemPerc\":\"20.97%\",\"PIDs\":\"2\",\"NetIO\":\"1kB / 2KiB\",\"BlockIO\":\"0B / 1MB\"}}";
    private static string Json(string? text) => text is null ? "null" : "\"" + text + "\"";
    private static DockerMonitoringTestsTransport Transport(string inspect, string? discovery = null, string? stats = null, bool statsFailure = false) => new(command =>
        command == "uname -s" ? new(0, "Linux\n", "") : command.StartsWith("docker info", StringComparison.Ordinal) ? new(0, "{\"ID\":\"engine\",\"OSType\":\"linux\"}", "")
            : command.StartsWith("docker ps", StringComparison.Ordinal) ? new(0, discovery ?? Discovery(Id, "app"), "")
            : command.StartsWith("docker stats", StringComparison.Ordinal) ? statsFailure ? new(1, "", "stats failed") : new(0, stats ?? "", "") : new(0, inspect, ""));
}
