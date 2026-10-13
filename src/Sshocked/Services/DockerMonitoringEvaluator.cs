using System.Globalization;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class DockerMonitoringEvaluator
{
    public static MonitoringObservation Evaluate(string engine, IReadOnlyList<DockerMonitoringContainer> containers,
        MonitorDefinition definition, MonitoringRecord? previous)
    {
        var observation = new MonitoringObservation { Snapshot = new() { Quality = MonitorDataQuality.Success } };
        var expected = MonitoringOptions.Get(definition, "expected", "running");
        var requireHealthy = MonitoringOptions.Boolean(definition, "requireHealthy");
        var minimum = MonitoringOptions.Get(definition, "targetMode", "name") == "compose" ? MonitoringOptions.Integer(definition, "minReplicas", 1, 1, 256) : 1;
        var exitCodes = DockerMonitoringProvider.ExitCodes(definition);
        var access = MonitoringOptions.Boolean(definition, "allowSudo").ToString();
        observation.State["engine.id"] = engine;
        observation.State["engine.sudo"] = access;
        var sameEngine = previous?.State.GetValueOrDefault("engine.id") == engine && previous.State.GetValueOrDefault("engine.sudo") == access;
        bool Expected(DockerMonitoringContainer item) => expected switch
        {
            "stopped" => item.Status is "exited" or "created",
            "job" => (item.Status == "running" && item.Running) || item.Status == "created" || (item.Status == "exited" && exitCodes.Contains(item.ExitCode)),
            _ => item.Status == "running" && item.Running
        };
        var ready = containers.Count(item => Expected(item) && (!requireHealthy || item.Health == "healthy"));
        Number(observation, "containers.total", "Matching containers", "count", containers.Count);
        Number(observation, "containers.ready", "Available replicas", "count", ready);
        Text(observation, "state", "Target state", containers.Count == 0 ? "missing" : containers.Count == 1 ? containers[0].Status : $"{ready}/{containers.Count} ready");
        Text(observation, "health", "Target health", containers.Count == 0 ? "unknown" : containers.Count == 1 ? containers[0].Health : string.Join(", ", containers.Select(item => item.Health).Distinct()));
        observation.Conditions.Add(new("target.availability", "Expected target availability", ready < minimum, MonitorSeverity.Critical,
            containers.Count == 0 ? "No matching target exists in fresh Docker discovery." : $"{ready} available replica(s); {minimum} required."));
        observation.Conditions.Add(new("target.state", "Unexpected container state", containers.Any(item => !Expected(item)), MonitorSeverity.Critical));
        observation.Conditions.Add(new("target.health", "Docker healthcheck", expected == "running" && containers.Any(item => item.Health == "unhealthy" || (requireHealthy && item.Health != "healthy")),
            MonitorSeverity.Critical, requireHealthy ? "An explicitly healthy healthcheck is required." : "Only reported unhealthy healthchecks trigger this condition."));
        observation.Conditions.Add(new("target.exitcode", "Unexpected exit code", expected != "stopped" && containers.Any(item => item.Status is "exited" or "dead" && !exitCodes.Contains(item.ExitCode)), MonitorSeverity.Critical));
        observation.Conditions.Add(new("target.oom", "Docker reports OOMKilled", containers.Any(item => item.OomKilled), MonitorSeverity.Critical));
        var totalIncrease = 0d;
        var baseline = false;
        foreach (var item in containers)
        {
            var prefix = "container." + item.Id + ".";
            Text(observation, prefix + "state", item.Name + " state", item.Status);
            Text(observation, prefix + "health", item.Name + " health", item.Health);
            Number(observation, prefix + "exitcode", item.Name + " exit code", "code", item.ExitCode);
            Number(observation, prefix + "oom", item.Name + " OOMKilled", "boolean", item.OomKilled ? 1 : 0);
            Number(observation, prefix + "restart.total", item.Name + " RestartCount", "count", item.RestartCount);
            Number(observation, prefix + "memory.configured.limit", item.Name + " configured memory limit", "bytes", item.MemoryLimit);
            var stateKey = "restart." + item.Id;
            observation.State[stateKey] = item.RestartCount.ToString(CultureInfo.InvariantCulture);
            double? increase = sameEngine && long.TryParse(previous!.State.GetValueOrDefault(stateKey), NumberStyles.None, CultureInfo.InvariantCulture, out var oldCount)
                && item.RestartCount >= oldCount ? item.RestartCount - oldCount : null;
            Number(observation, prefix + "restart.increase", item.Name + " observed RestartCount increase", "count", increase, MonitorDataQuality.Baseline);
            if (increase is { } known) totalIncrease += known;
            else baseline = true;
        }
        Number(observation, "restart.increase", "Observed RestartCount increase since previous successful inspection", "count", baseline ? null : totalIncrease, MonitorDataQuality.Baseline);
        observation.Conditions.Add(new("target.restart", "Observed RestartCount increase", totalIncrease > 0 ? true : baseline ? null : false,
            MonitorSeverity.Warning, "A counter difference is an observed increase; its events cannot be assigned exact times during offline periods."));
        if (baseline) observation.Snapshot.Quality = MonitorDataQuality.Partial;
        return observation;
    }

    internal static void Number(MonitoringObservation observation, string key, string name, string unit, double? value,
        MonitorDataQuality absent = MonitorDataQuality.Unsupported) => observation.Snapshot.Metrics.Add(new()
        { Key = key, Name = name, Unit = unit, NumericValue = value, Quality = value is { } number && double.IsFinite(number) ? MonitorDataQuality.Success : absent, Source = "Docker CLI" });

    private static void Text(MonitoringObservation observation, string key, string name, string value) =>
        observation.Snapshot.Metrics.Add(new() { Key = key, Name = name, TextValue = value, Source = "Docker inspect" });
}
