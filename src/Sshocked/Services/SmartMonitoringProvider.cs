using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SmartMonitoringProvider(IMonitoringTransport transport) : IMonitoringProvider
{
    public string Key => "smart";
    public string Kind => "server";
    public IReadOnlyList<MonitorOption> Options { get; } = [
        new("platform", "Remote platform", "linux", Choices: ["linux", "windows"]),
        new("device", "Linux device path or Windows physical disk name", "/dev/sda", true),
        new("allowSudo", "Explicitly permit configured sudo access", "false", Choices: ["false", "true"])];

    public void Validate(MonitorDefinition definition)
    {
        var platform = MonitoringOptions.Get(definition, "platform", "linux");
        var device = MonitoringOptions.Get(definition, "device", "/dev/sda");
        if (platform is not ("linux" or "windows") || device.Length is < 1 or > 256 || device.Any(char.IsControl))
            throw new ArgumentException("Select a supported platform and a literal disk identifier.");
        var sudo = MonitoringOptions.Boolean(definition, "allowSudo");
        if (platform == "linux" && !device.StartsWith("/dev/", StringComparison.Ordinal))
            throw new ArgumentException("Linux SMART devices must be explicit /dev paths.");
        if (platform == "windows" && sudo) throw new ArgumentException("Sudo is not supported on Windows.");
    }

    public async Task<MonitoringObservation> Collect(ServerHost host, MonitorDefinition definition,
        MonitoringRecord? previous, CancellationToken cancellationToken = default)
    {
        var windows = MonitoringOptions.Get(definition, "platform", "linux") == "windows";
        var device = MonitoringOptions.Get(definition, "device", "/dev/sda");
        var script = windows
            ? MonitoringOptions.PowerShellScript(
                "if(-not(Get-Command Get-StorageReliabilityCounter -ErrorAction SilentlyContinue)){exit 127};" +
                "$disk=Get-PhysicalDisk|Where-Object FriendlyName -EQ '" + device.Replace("'", "''") + "';" +
                "if(-not $disk){exit 2};$r=$disk|Get-StorageReliabilityCounter;" +
                "[pscustomobject]@{health=[string]$disk.HealthStatus;temperature=$r.Temperature;wear=$r.Wear} | ConvertTo-Json -Compress")
            : "smartctl -a -j -- " + RemoteShell.Quote(device);
        var result = await transport.Execute(host, script, MonitoringProbeResult.Options(definition,
            MonitoringOptions.Boolean(definition, "allowSudo")), cancellationToken);
        if (windows)
        {
            MonitoringProbeResult.Ensure(result);
            return ParseWindows(result.StandardOutput);
        }
        if (string.IsNullOrWhiteSpace(result.StandardOutput) || !result.StandardOutput.TrimStart().StartsWith('{'))
            MonitoringProbeResult.Ensure(result);
        return ParseLinux(result.StandardOutput, result.ExitCode);
    }

    public static MonitoringObservation ParseLinux(string output, int exitCode)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (!root.TryGetProperty("smartctl", out _)) throw new InvalidDataException("The SMART probe returned invalid JSON.");
        var observation = new MonitoringObservation();
        bool? passed = root.TryGetProperty("smart_status", out var status) && status.TryGetProperty("passed", out var passedValue)
            && passedValue.ValueKind is JsonValueKind.True or JsonValueKind.False ? passedValue.GetBoolean() : null;
        ServerMonitoringProvider.Add(observation, "smart.passed", "SMART health passed", "boolean", passed is { } health ? health ? 1 : 0 : null, "smartctl");
        if (root.TryGetProperty("temperature", out var temperature) && temperature.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var degrees))
            ServerMonitoringProvider.Add(observation, "smart.temperature", "Disk temperature", "C", degrees, "smartctl");
        if (root.TryGetProperty("nvme_smart_health_information_log", out var nvme))
        {
            foreach (var key in new[] { "critical_warning", "percentage_used", "media_errors" })
                if (nvme.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetDouble(out var number))
                    ServerMonitoringProvider.Add(observation, "nvme." + key, key, "raw", number, "smartctl NVMe");
            var warning = nvme.TryGetProperty("critical_warning", out var fieldWarning) && fieldWarning.ValueKind == JsonValueKind.Number && fieldWarning.TryGetInt32(out var bits) ? (bool?)(bits != 0) : null;
            observation.Conditions.Add(new("nvme.critical", "NVMe critical warning", warning, MonitorSeverity.Critical));
        }
        observation.Conditions.Add(new("smart.failed", "SMART health failure", passed == false || (exitCode & 8) != 0
            ? true : passed == true ? false : null, MonitorSeverity.Critical));
        observation.Conditions.Add(new("smart.history", "SMART attribute/error-log warning",
            (exitCode & 240) != 0 ? true : (exitCode & 7) != 0 ? null : false, MonitorSeverity.Warning));
        observation.Snapshot.Quality = (exitCode & 7) != 0 || passed is null ? MonitorDataQuality.Partial : MonitorDataQuality.Success;
        if ((exitCode & 7) != 0) observation.Snapshot.Message = "SMART returned access/device/command diagnostic bits; available health values are partial.";
        return observation;
    }

    public static MonitoringObservation ParseWindows(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var observation = new MonitoringObservation();
        var health = root.TryGetProperty("health", out var status) ? status.GetString() : null;
        observation.Snapshot.Metrics.Add(new()
        {
            Key = "storage.health",
            Name = "Storage health",
            TextValue = health,
            Source = "Windows Storage",
            Quality = health is null ? MonitorDataQuality.Unsupported : MonitorDataQuality.Success
        });
        foreach (var key in new[] { "temperature", "wear" })
            ServerMonitoringProvider.Add(observation, "storage." + key, key, key == "temperature" ? "C" : "%",
                root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric) ? numeric : null, "StorageReliabilityCounter");
        observation.Conditions.Add(new("storage.unhealthy", "Storage health warning", health is null ? null : health != "Healthy"));
        observation.Snapshot.Quality = observation.Snapshot.Metrics.All(metric => metric.Quality == MonitorDataQuality.Success)
            ? MonitorDataQuality.Success : MonitorDataQuality.Partial;
        return observation;
    }
}
