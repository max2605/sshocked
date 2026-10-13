using System.Globalization;
using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class DockerMonitoringProvider(IMonitoringTransport transport) : IMonitoringProvider
{
    public string Key => "container";
    public string Kind => "container";
    public IReadOnlyList<MonitorOption> Options { get; } = [
        new("platform", "Remote platform", "linux", Choices: ["linux"]),
        new("targetMode", "Target identity", "name", Choices: ["name", "id", "compose"]),
        new("target", "Exact container name or full ID (empty for Compose)"),
        new("project", "Exact Compose project (Compose only)"),
        new("service", "Exact Compose service (Compose only)"),
        new("expected", "Expected state", "running", Choices: ["running", "stopped", "job"]),
        new("expectedExitCodes", "Successful job exit codes, comma-separated", "0"),
        new("minReplicas", "Minimum available Compose replicas", "1"),
        new("requireHealthy", "Require an explicitly healthy Docker healthcheck", "false", Choices: ["false", "true"]),
        new("collectStats", "Collect resource statistics", "true", Choices: ["true", "false"]),
        new("allowSudo", "Use configured sudo policy (different Docker endpoint)", "false", Choices: ["false", "true"])];

    public void Validate(MonitorDefinition definition)
    {
        if (MonitoringOptions.Get(definition, "platform", "linux") != "linux") throw new NotSupportedException("Docker monitoring currently supports remote Linux only.");
        var mode = MonitoringOptions.Get(definition, "targetMode", "name");
        if (mode is not ("name" or "id" or "compose")) throw new ArgumentException("Target mode must be name, id or compose.");
        if (MonitoringOptions.Get(definition, "expected", "running") is not ("running" or "stopped" or "job")) throw new ArgumentException("Expected state must be running, stopped or job.");
        var target = MonitoringOptions.Get(definition, "target");
        if (mode == "id" && !DockerMonitoringParser.IsFullId(target)) throw new ArgumentException("ID monitoring requires the full 64-character hexadecimal Docker ID.");
        if (mode == "name") ValidateText(target, "Container name");
        if (mode == "compose") { ValidateText(MonitoringOptions.Get(definition, "project"), "Compose project"); ValidateText(MonitoringOptions.Get(definition, "service"), "Compose service"); }
        MonitoringOptions.Integer(definition, "minReplicas", 1, 1, 256);
        if (MonitoringOptions.Boolean(definition, "requireHealthy") && MonitoringOptions.Get(definition, "expected", "running") != "running")
            throw new ArgumentException("RequireHealthy applies only to the running expectation.");
        MonitoringOptions.Boolean(definition, "collectStats", true);
        MonitoringOptions.Boolean(definition, "allowSudo");
        ExitCodes(definition);
    }

    public Task<MonitoringObservation> Collect(ServerHost host, MonitorDefinition definition, MonitoringRecord? previous,
        CancellationToken cancellationToken = default) => CollectAttempt(host, definition, previous, false, cancellationToken);

    private async Task<MonitoringObservation> CollectAttempt(ServerHost host, MonitorDefinition definition, MonitoringRecord? previous,
        bool retried, CancellationToken ct)
    {
        Validate(definition);
        var sudo = MonitoringOptions.Boolean(definition, "allowSudo");
        Task<ProcessResult> Execute(string command) => transport.Execute(host, command, MonitoringProbeResult.Options(definition, sudo), ct);
        var platform = await transport.Execute(host, "uname -s", MonitoringProbeResult.Options(definition), ct);
        MonitoringProbeResult.Ensure(platform);
        if (platform.StandardOutput.Trim() != "Linux") throw new NotSupportedException("Docker monitoring currently supports remote Linux only.");
        var info = await Execute(DockerMonitoringCommands.Info);
        MonitoringProbeResult.Ensure(info);
        using var infoDocument = JsonDocument.Parse(info.StandardOutput);
        if (DockerMonitoringParser.Required(infoDocument.RootElement, "OSType") != "linux") throw new NotSupportedException("Only Linux Docker Engine metrics are supported.");
        var engine = DockerMonitoringParser.Required(infoDocument.RootElement, "ID");
        var discovery = await Execute(DockerMonitoringCommands.Discovery);
        MonitoringProbeResult.Ensure(discovery);
        var candidates = DockerMonitoringParser.Read(discovery.StandardOutput, DockerMonitoringParser.Discovery);
        var mode = MonitoringOptions.Get(definition, "targetMode", "name");
        var selected = candidates.Where(item => mode switch
        {
            "name" => item.Name == MonitoringOptions.Get(definition, "target"),
            "id" => item.Id.Equals(MonitoringOptions.Get(definition, "target"), StringComparison.OrdinalIgnoreCase),
            _ => item.Project == MonitoringOptions.Get(definition, "project") && item.Service == MonitoringOptions.Get(definition, "service")
        }).DistinctBy(item => item.Id).ToArray();
        if (selected.Length > 256) throw new NotSupportedException("At most 256 replicas can be monitored by one definition. Narrow the Compose service target.");
        var containers = new List<DockerMonitoringContainer>();
        foreach (var batch in selected.Chunk(32))
        {
            var inspected = await Execute(DockerMonitoringCommands.Inspect(batch.Select(item => item.Id)));
            if (inspected.ExitCode != 0 && !retried && (inspected.StandardError.Contains("No such", StringComparison.OrdinalIgnoreCase)))
                return await CollectAttempt(host, definition, previous, true, ct);
            MonitoringProbeResult.Ensure(inspected);
            var parsed = DockerMonitoringParser.Read(inspected.StandardOutput, DockerMonitoringParser.Container);
            if (parsed.Count != batch.Length || parsed.Any(item => !batch.Any(expected => expected.Id == item.Id)))
                throw new InvalidDataException("Docker inspect did not return the selected identities.");
            if (parsed.Any(item => mode == "name" && item.Name != MonitoringOptions.Get(definition, "target")
                || mode == "compose" && (item.Project != MonitoringOptions.Get(definition, "project") || item.Service != MonitoringOptions.Get(definition, "service"))))
            {
                if (!retried) return await CollectAttempt(host, definition, previous, true, ct);
                throw new InvalidDataException("Docker target membership changed during inspection. Retry on the next poll.");
            }
            containers.AddRange(parsed);
        }
        var observation = DockerMonitoringEvaluator.Evaluate(engine, containers, definition, previous);
        if (MonitoringOptions.Boolean(definition, "collectStats", true))
        {
            var running = containers.Where(item => item.Status == "running" && item.Running).Select(item => item.Id).ToArray();
            var output = new List<string>();
            var statsError = false;
            foreach (var batch in running.Chunk(32))
            {
                var stats = await Execute(DockerMonitoringCommands.Stats(batch));
                if (stats.ExitCode != 0) statsError = true;
                else output.Add(stats.StandardOutput);
            }
            DockerMonitoringStats.Add(observation, containers, string.Join('\n', output), statsError);
        }
        return observation;
    }

    internal static int[] ExitCodes(MonitorDefinition definition)
    {
        var values = MonitoringOptions.Get(definition, "expectedExitCodes", "0").Split(',', StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Length > 32 || values.Any(value => !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var code) || code is < 0 or > 255))
            throw new ArgumentException("Expected exit codes must be comma-separated integers from 0 to 255.");
        return values.Select(value => int.Parse(value, CultureInfo.InvariantCulture)).Distinct().ToArray();
    }

    private static void ValidateText(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256 || text.Any(character => character is '\0' or '\r' or '\n'))
            throw new ArgumentException($"{label} must be nonempty, at most 256 characters, and contain no line breaks or NUL.");
    }
}
