using System.Globalization;
using System.Text.Json;

namespace Sshocked.Services;

internal static class DockerMonitoringParser
{
    public static IReadOnlyList<T> Read<T>(string output, Func<JsonElement, T> parse)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];
        if (output.TrimStart().StartsWith('['))
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.EnumerateArray().Select(parse).ToArray();
        }
        return output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return parse(document.RootElement);
        }).ToArray();
    }

    public static DockerMonitoringDiscovery Discovery(JsonElement value) => new(Id(value), Required(value, "Name").TrimStart('/'), Optional(value, "Project"), Optional(value, "Service"));

    public static DockerMonitoringContainer Container(JsonElement value)
    {
        var state = Required(value, "Status");
        if (state is not ("created" or "running" or "paused" or "restarting" or "exited" or "dead" or "removing"))
            throw new InvalidDataException("Docker returned an unknown container state.");
        var health = Optional(value, "Health") ?? "notConfigured";
        if (health is not ("healthy" or "unhealthy" or "starting" or "notConfigured")) throw new InvalidDataException("Docker returned an unknown health state.");
        var count = Integer(value, "RestartCount");
        var limit = Integer(value, "MemoryLimit");
        if (count < 0 || limit < 0) throw new InvalidDataException("Docker counters must be nonnegative.");
        return new(Id(value), Required(value, "Name").TrimStart('/'), state, value.GetProperty("Running").GetBoolean(),
            checked((int)Integer(value, "ExitCode")), value.GetProperty("OOMKilled").GetBoolean(), count, health,
            limit == 0 ? null : limit, Optional(value, "Project"), Optional(value, "Service"));
    }

    public static bool IsFullId(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    public static string Id(JsonElement value)
    {
        var id = Required(value, "Id");
        return IsFullId(id) ? id : throw new InvalidDataException("Docker discovery did not return a full container ID.");
    }

    public static string Required(JsonElement value, string key) => Optional(value, key) is { Length: > 0 } text ? text : throw new InvalidDataException($"Docker response is missing {key}.");
    public static string? Optional(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static long Integer(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.TryGetInt64(out var number)
        ? number : throw new InvalidDataException($"Docker response has an invalid {key}.");

    public static double? Percent(string? text) => double.TryParse(text?.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value >= 0 ? value : null;

    public static double? Size(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        var split = 0;
        while (split < text.Length && (char.IsAsciiDigit(text[split]) || text[split] == '.')) split++;
        if (!double.TryParse(text[..split], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0) return null;
        var multiplier = text[split..].Trim().ToLowerInvariant() switch
        {
            "b" => 1d,
            "kb" => 1e3,
            "mb" => 1e6,
            "gb" => 1e9,
            "tb" => 1e12,
            "kib" => 1024d,
            "mib" => Math.Pow(1024, 2),
            "gib" => Math.Pow(1024, 3),
            "tib" => Math.Pow(1024, 4),
            _ => double.NaN
        };
        return double.IsFinite(value * multiplier) ? value * multiplier : null;
    }
}
