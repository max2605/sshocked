using System.Globalization;
using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class UpdateMonitoringProvider(IMonitoringTransport transport, TimeProvider clock) : IMonitoringProvider
{
    public string Key => "updates";
    public string Kind => "server";
    public IReadOnlyList<MonitorOption> Options { get; } = [
        new("platform", "Remote platform", "linux", Choices: ["linux", "windows"]),
        new("warnUpdates", "Warn when upgrade candidates exist", "true", Choices: ["true", "false"]),
        new("maximumIndexAgeHours", "Maximum acceptable package-index age in hours", "48")];

    public void Validate(MonitorDefinition definition)
    {
        if (MonitoringOptions.Get(definition, "platform", "linux") is not ("linux" or "windows"))
            throw new ArgumentException("Update checks support Debian/Ubuntu and Windows.");
        _ = MonitoringOptions.Boolean(definition, "warnUpdates", true);
        _ = MonitoringOptions.Integer(definition, "maximumIndexAgeHours", 48, 1, 8760);
    }

    public async Task<MonitoringObservation> Collect(ServerHost host, MonitorDefinition definition,
        MonitoringRecord? previous, CancellationToken cancellationToken = default)
    {
        var windows = MonitoringOptions.Get(definition, "platform", "linux") == "windows";
        var result = await transport.Execute(host, windows ? MonitoringOptions.PowerShellScript(UpdateMonitorCommands.Windows)
            : MonitoringOptions.PosixScript(UpdateMonitorCommands.Linux), MonitoringProbeResult.Options(definition), cancellationToken);
        MonitoringProbeResult.Ensure(result);
        return windows ? ParseWindows(result.StandardOutput, definition) : ParseLinux(result.StandardOutput, definition, clock.GetUtcNow());
    }

    public static MonitoringObservation ParseLinux(string output, MonitorDefinition definition, DateTimeOffset now)
    {
        var fields = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 2)).ToDictionary(parts => parts[0], parts => parts.Length == 2 ? parts[1] : "", StringComparer.Ordinal);
        if (!int.TryParse(fields.GetValueOrDefault("UPDATES"), out var count) || count < 0)
            throw new InvalidDataException("The package simulation did not return a candidate count.");
        var hasIndex = double.TryParse(fields.GetValueOrDefault("INDEX"), NumberStyles.Float, CultureInfo.InvariantCulture, out var unix) &&
            double.IsFinite(unix) && unix > 0 && unix <= now.ToUnixTimeSeconds();
        var age = hasIndex ? now.ToUnixTimeSeconds() - unix : (double?)null;
        var fresh = age is not null && age <= MonitoringOptions.Integer(definition, "maximumIndexAgeHours", 48, 1, 8760) * 3600d;
        var observation = new MonitoringObservation();
        ServerMonitoringProvider.Add(observation, "updates.candidates", "Upgrade candidates (existing index)", "packages", hasIndex ? count : null, "apt-get --simulate upgrade");
        ServerMonitoringProvider.Add(observation, "updates.indexAge", "Newest observed package-index age", "seconds", age, "/var/lib/apt/lists");
        var reboot = fields.GetValueOrDefault("REBOOT") == "1";
        ServerMonitoringProvider.Add(observation, "updates.rebootRequired", "Host reboot marker", "boolean", reboot ? 1 : 0, "/var/run/reboot-required");
        observation.Conditions.Add(new("updates.available", "Updates available", MonitoringOptions.Boolean(definition, "warnUpdates", true) && hasIndex ? count > 0 : hasIndex ? false : null));
        observation.Conditions.Add(new("updates.indexStale", "Package index is unavailable or old", !fresh));
        observation.Conditions.Add(new("updates.reboot", "Host reboot indicated", reboot));
        observation.Snapshot.Quality = fresh ? MonitorDataQuality.Success : MonitorDataQuality.Partial;
        observation.Snapshot.Message = "Candidates follow an apt-get upgrade simulation using existing indices; no indices or packages were changed. Security classification is unavailable.";
        return observation;
    }

    public static MonitoringObservation ParseWindows(string output, MonitorDefinition definition)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var successful = root.TryGetProperty("resultCode", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var resultCode) && resultCode == 2;
        var count = root.TryGetProperty("updates", out var candidates) && candidates.ValueKind == JsonValueKind.Number && candidates.TryGetInt32(out var value) && value >= 0 ? value : (int?)null;
        var reboot = root.TryGetProperty("reboot", out var restart) && restart.ValueKind is JsonValueKind.True or JsonValueKind.False ? restart.GetBoolean() : (bool?)null;
        var observation = new MonitoringObservation();
        ServerMonitoringProvider.Add(observation, "updates.candidates", "Windows Update candidates", "updates", successful ? count : null, "Windows Update Agent Search");
        ServerMonitoringProvider.Add(observation, "updates.rebootRequired", "Windows host reboot requirement", "boolean", reboot is { } needs ? needs ? 1 : 0 : null, "Windows Update Agent SystemInfo");
        observation.Conditions.Add(new("updates.available", "Updates available", successful && count is not null
            ? MonitoringOptions.Boolean(definition, "warnUpdates", true) && count > 0 : null));
        observation.Conditions.Add(new("updates.reboot", "Host reboot indicated", reboot));
        observation.Snapshot.Quality = successful && count is not null && reboot is not null ? MonitorDataQuality.Success : MonitorDataQuality.Partial;
        observation.Snapshot.Message = "Read-only Windows Update Agent search; configured update policy and network access apply.";
        return observation;
    }
}
