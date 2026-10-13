using System.Globalization;
using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Services;

public static class ProcessMonitorParsing
{
    public static double? Number(string? text) => double.TryParse(text, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0 ? number : null;
    public static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined ? value.ToString() : "";
    public static double? Number(JsonElement element, string name) => Number(Text(element, name));
    public static bool Boolean(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;
    public static MonitorMetric Metric(string key, string name, string? text, string source) =>
        new()
        {
            Key = key,
            Name = name,
            TextValue = text,
            Source = source,
            Quality = text is null ? MonitorDataQuality.Unsupported : MonitorDataQuality.Success
        };
    public static MonitorMetric Metric(string key, string name, double? number, string unit, string source,
        MonitorDataQuality missing = MonitorDataQuality.Unsupported) => new()
        {
            Key = key,
            Name = name,
            NumericValue = number,
            Unit = unit,
            Source = source,
            Quality = number is null ? missing : MonitorDataQuality.Success
        };

    public static MonitorSeverity Severity(MonitorDefinition definition)
    {
        if (!Enum.TryParse<MonitorSeverity>(MonitoringOptions.Get(definition, "severity", "Warning"), true, out var severity)
            || severity is not (MonitorSeverity.Warning or MonitorSeverity.Critical)) throw new ArgumentException("severity must be warning or critical.");
        return severity;
    }

    public static void ValidateText(string text, string label, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(text) || text.Length > 512 || text.Any(char.IsControl) || text.StartsWith('-'))
            throw new ArgumentException($"{label} must be a valid data value without control characters or a leading '-'.");
    }

    public static void ValidatePlatform(MonitorDefinition definition)
    {
        if (MonitoringOptions.Get(definition, "platform", "linux") is not ("linux" or "windows")) throw new ArgumentException("platform must be linux or windows.");
        _ = Severity(definition);
    }

    public static string State(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    public static double? Previous(MonitoringRecord? previous, string key) => previous?.State.TryGetValue(key, out var value) == true ? Number(value) : null;
    public static string PowerShellQuote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
