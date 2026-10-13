using System.Globalization;
using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class DockerMonitoringStats
{
    public static void Add(MonitoringObservation observation, IReadOnlyList<DockerMonitoringContainer> containers, string output, bool failed)
    {
        var rows = new Dictionary<string, Dictionary<string, double?>>(StringComparer.Ordinal);
        try
        {
            foreach (var row in DockerMonitoringParser.Read(output, ReadRow))
                if (!rows.TryAdd(row.Key, row.Value)) throw new InvalidDataException("Docker stats returned duplicate container IDs.");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException) { failed = true; rows.Clear(); }
        var cpu = new List<double?>();
        var memory = new List<double?>();
        var limits = new List<double?>();
        foreach (var container in containers)
        {
            var values = container.Status == "running" ? rows.GetValueOrDefault(container.Id) : null;
            double? Get(string key) => values?.GetValueOrDefault(key);
            var prefix = "container." + container.Id + ".";
            DockerMonitoringEvaluator.Number(observation, prefix + "cpu.percent", container.Name + " CPU (Docker scale)", "%", Get("cpu"));
            DockerMonitoringEvaluator.Number(observation, prefix + "memory.bytes", container.Name + " memory excluding reclaimable cache", "bytes", Get("memory"));
            DockerMonitoringEvaluator.Number(observation, prefix + "memory.reported.limit", container.Name + " Docker reported memory limit", "bytes", Get("limit"));
            DockerMonitoringEvaluator.Number(observation, prefix + "memory.percent", container.Name + " memory / Docker reported limit", "%", Get("memory.percent"));
            DockerMonitoringEvaluator.Number(observation, prefix + "pids", container.Name + " processes and threads", "count", Get("pids"));
            foreach (var (key, name) in new[] { ("network.rx", "network received"), ("network.tx", "network sent"), ("block.read", "block read"), ("block.write", "block written") })
                DockerMonitoringEvaluator.Number(observation, prefix + key, container.Name + " " + name, "bytes", Get(key));
            cpu.Add(Get("cpu"));
            memory.Add(Get("memory"));
            limits.Add(Get("limit"));
        }
        double? Sum(List<double?> values) => values.Count > 0 && values.All(value => value is not null) ? values.Sum(value => value!.Value) : null;
        var memorySum = Sum(memory);
        var limitSum = Sum(limits);
        DockerMonitoringEvaluator.Number(observation, "cpu.percent", "Total Docker CPU (not normalized to host capacity)", "%", Sum(cpu));
        DockerMonitoringEvaluator.Number(observation, "memory.bytes", "Total Docker memory excluding reclaimable cache", "bytes", memorySum);
        DockerMonitoringEvaluator.Number(observation, "memory.percent", "Memory / total Docker reported limits", "%", limitSum > 0 ? 100 * memorySum / limitSum : null);
        if (failed || containers.Any(item => item.Status == "running" && (!rows.TryGetValue(item.Id, out var values)
            || values.GetValueOrDefault("cpu") is null || values.GetValueOrDefault("memory") is null || values.GetValueOrDefault("limit") is null)))
        {
            observation.Snapshot.Quality = MonitorDataQuality.Partial;
            observation.Snapshot.Message = "Some Docker stats are unavailable; successfully inspected container states remain usable.";
        }
    }

    private static KeyValuePair<string, Dictionary<string, double?>> ReadRow(JsonElement element)
    {
        var id = DockerMonitoringParser.Required(element, "ID");
        if (!DockerMonitoringParser.IsFullId(id)) throw new InvalidDataException("Docker stats did not return a full container ID.");
        string? Get(string key) => DockerMonitoringParser.Optional(element, key);
        var memory = (Get("MemUsage") ?? "").Split('/', 2, StringSplitOptions.TrimEntries);
        var values = new Dictionary<string, double?>
        {
            ["cpu"] = DockerMonitoringParser.Percent(Get("CPUPerc")),
            ["memory.percent"] = DockerMonitoringParser.Percent(Get("MemPerc")),
            ["memory"] = DockerMonitoringParser.Size(memory[0]),
            ["limit"] = memory.Length == 2 ? DockerMonitoringParser.Size(memory[1]) : null,
            ["pids"] = int.TryParse(Get("PIDs"), NumberStyles.None, CultureInfo.InvariantCulture, out var pids) && pids >= 0 ? pids : null
        };
        foreach (var (field, first, second) in new[] { ("NetIO", "network.rx", "network.tx"), ("BlockIO", "block.read", "block.write") })
        {
            var parts = (Get(field) ?? "").Split('/', 2, StringSplitOptions.TrimEntries);
            values[first] = DockerMonitoringParser.Size(parts[0]);
            values[second] = parts.Length == 2 ? DockerMonitoringParser.Size(parts[1]) : null;
        }
        return new(id, values);
    }
}
