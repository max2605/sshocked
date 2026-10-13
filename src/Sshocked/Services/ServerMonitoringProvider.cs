using System.Globalization;
using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ServerMonitoringProvider(IMonitoringTransport transport) : IMonitoringProvider
{
    public string Key => "server";
    public string Kind => "server";
    public IReadOnlyList<MonitorOption> Options { get; } = [
        new("platform", "Remote platform", "auto", Choices: ["auto", "linux", "windows"])];

    public void Validate(MonitorDefinition definition)
    {
        if (MonitoringOptions.Get(definition, "platform", "auto") is not ("auto" or "linux" or "windows"))
            throw new ArgumentException("Platform must be auto, linux or windows.");
    }

    public async Task<MonitoringObservation> Collect(ServerHost host, MonitorDefinition definition,
        MonitoringRecord? previous, CancellationToken cancellationToken = default)
    {
        var platform = MonitoringOptions.Get(definition, "platform", "auto");
        if (platform == "auto")
        {
            var detection = await transport.Execute(host, "uname -s", MonitoringProbeResult.Options(definition), cancellationToken);
            if (detection.ExitCode == 255) MonitoringProbeResult.Ensure(detection);
            if (detection.ExitCode == 0 && detection.StandardOutput.Trim() != "Linux")
                throw new NotSupportedException("Server metrics currently support remote Linux and Windows.");
            platform = detection.ExitCode == 0 ? "linux" : "windows";
        }
        var result = await transport.Execute(host, platform == "linux"
            ? MonitoringOptions.PosixScript(ServerMonitorCommands.Linux) : MonitoringOptions.PowerShellScript(ServerMonitorCommands.Windows),
            MonitoringProbeResult.Options(definition), cancellationToken);
        MonitoringProbeResult.Ensure(result);
        return platform == "linux" ? ParseLinux(result.StandardOutput, previous) : ParseWindows(result.StandardOutput);
    }

    public static MonitoringObservation ParseLinux(string output, MonitoringRecord? previous)
    {
        var observation = new MonitoringObservation();
        var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var boot = lines.FirstOrDefault(line => line.StartsWith("BOOT ", StringComparison.Ordinal))?[5..];
        if (string.IsNullOrWhiteSpace(boot)) throw new InvalidDataException("Linux probe did not return its boot identity.");
        observation.State["boot"] = boot;
        var cpuLine = lines.FirstOrDefault(line => line.StartsWith("cpu ", StringComparison.Ordinal));
        var cpu = new MonitorMetric { Key = "cpu.percent", Name = "CPU utilization", Unit = "%", Source = "/proc/stat", Quality = MonitorDataQuality.Baseline };
        if (cpuLine is not null)
        {
            var counters = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(Number).ToArray();
            if (counters.Length < 4) throw new InvalidDataException("Linux CPU counters are incomplete.");
            var total = counters.Sum();
            var idle = counters[3] + (counters.Length > 4 ? counters[4] : 0);
            observation.State["cpu.total"] = total.ToString("R", CultureInfo.InvariantCulture);
            observation.State["cpu.idle"] = idle.ToString("R", CultureInfo.InvariantCulture);
            if (previous?.State.GetValueOrDefault("boot") == boot &&
                double.TryParse(previous.State.GetValueOrDefault("cpu.total"), CultureInfo.InvariantCulture, out var oldTotal) &&
                double.TryParse(previous.State.GetValueOrDefault("cpu.idle"), CultureInfo.InvariantCulture, out var oldIdle) &&
                total > oldTotal && idle >= oldIdle && idle - oldIdle <= total - oldTotal)
            {
                cpu.NumericValue = 100 * (1 - (idle - oldIdle) / (total - oldTotal));
                cpu.Quality = MonitorDataQuality.Success;
            }
        }
        else cpu.Quality = MonitorDataQuality.Unsupported;
        observation.Snapshot.Metrics.Add(cpu);
        double? Field(string name)
        {
            var text = lines.FirstOrDefault(line => line.StartsWith(name + " ", StringComparison.Ordinal));
            return text is null ? null : Number(text[(name.Length + 1)..].Split(' ')[0]);
        }
        var memory = Field("MemTotal:");
        var available = Field("MemAvailable:");
        Add(observation, "memory.total", "Total memory", "bytes", memory * 1024, "/proc/meminfo");
        Add(observation, "memory.available", "Available memory", "bytes", available * 1024, "/proc/meminfo");
        Add(observation, "memory.percent", "Memory utilization", "%", memory > 0 && available is not null ? 100 * (1 - available / memory) : null, "/proc/meminfo");
        Add(observation, "swap.total", "Swap total", "bytes", Field("SwapTotal:") * 1024, "/proc/meminfo");
        Add(observation, "swap.free", "Swap free", "bytes", Field("SwapFree:") * 1024, "/proc/meminfo");
        Add(observation, "uptime.seconds", "Uptime", "seconds", Field("UPTIME"), "/proc/uptime");
        var marker = Array.IndexOf(lines, "FS_BEGIN");
        if (marker >= 0)
            foreach (var line in lines.Skip(marker + 2))
            {
                var columns = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length != 6 || !double.TryParse(columns[4].TrimEnd('%'), CultureInfo.InvariantCulture, out var percent)) continue;
                Add(observation, "disk." + columns[5] + ".percent", "Filesystem " + columns[5], "%", percent, "df -Pk");
                Add(observation, "disk." + columns[5] + ".available", "Filesystem available " + columns[5], "bytes", Number(columns[3]) * 1024, "df -Pk");
            }
        observation.Snapshot.Quality = observation.Snapshot.Metrics.All(metric => metric.Quality == MonitorDataQuality.Success)
            ? MonitorDataQuality.Success : MonitorDataQuality.Partial;
        return observation;
    }

    public static MonitoringObservation ParseWindows(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var observation = new MonitoringObservation();
        double? Value(JsonElement element, string key) => element.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetDouble(out var value) ? value : null;
        var total = Value(root, "total");
        var available = Value(root, "available");
        Add(observation, "cpu.percent", "CPU utilization", "%", Value(root, "cpu"), "Windows CIM");
        Add(observation, "memory.total", "Total memory", "bytes", total, "Windows CIM");
        Add(observation, "memory.available", "Available memory", "bytes", available, "Windows CIM");
        Add(observation, "memory.percent", "Memory utilization", "%", total > 0 && available is not null ? 100 * (1 - available / total) : null, "Windows CIM");
        Add(observation, "uptime.seconds", "Uptime", "seconds", Value(root, "uptime"), "Windows CIM");
        if (root.TryGetProperty("volumes", out var volumes) && volumes.ValueKind == JsonValueKind.Array)
            foreach (var volume in volumes.EnumerateArray())
            {
                var name = volume.GetProperty("name").GetString() ?? "unknown";
                var size = Value(volume, "size");
                var free = Value(volume, "available");
                Add(observation, "disk." + name + ".percent", "Volume " + name, "%", size > 0 && free is not null ? 100 * (1 - free / size) : null, "Windows CIM");
            }
        observation.Snapshot.Quality = observation.Snapshot.Metrics.All(metric => metric.Quality == MonitorDataQuality.Success)
            ? MonitorDataQuality.Success : MonitorDataQuality.Partial;
        return observation;
    }

    public static void Add(MonitoringObservation observation, string key, string name, string unit, double? value, string source) =>
        observation.Snapshot.Metrics.Add(new()
        {
            Key = key,
            Name = name,
            Unit = unit,
            NumericValue = value is { } number && double.IsFinite(number) ? number : null,
            Source = source,
            Quality = value is { } numeric && double.IsFinite(numeric) ? MonitorDataQuality.Success : MonitorDataQuality.Unsupported
        });

    private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
        ? value : throw new InvalidDataException("Remote numeric data is invalid.");
}
