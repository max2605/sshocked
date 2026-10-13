using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class DockerJsonParser
{
    public static void ReadHostInfo(string raw, DockerHostInfo info)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ServerVersion", out _)) throw new JsonException("Missing Docker server version.");
        info.ServerVersion = String(root, "ServerVersion");
        info.OsType = String(root, "OSType");
        info.Architecture = String(root, "Architecture");
        info.ContainersRunning = Number(root, "ContainersRunning");
        info.ContainersTotal = Number(root, "Containers");
    }

    public static List<ContainerModel> ReadContainers(string raw) => Read(raw, root =>
    {
        var id = String(root, "ID");
        var name = String(root, "Names");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new JsonException("Container identity is missing.");
        return new ContainerModel
        {
            ContainerId = id,
            Name = name.TrimStart('/'),
            Image = String(root, "Image") ?? string.Empty,
            Status = String(root, "Status") ?? string.Empty,
            Ports = String(root, "Ports"),
            ComposeProject = String(root, "ComposeProject")
        };
    });

    public static List<ComposeStackModel> ReadComposeStacks(string raw) => Read(raw, root => new ComposeStackModel
    {
        Name = String(root, "Name") ?? throw new JsonException("Compose project name is missing."),
        Status = String(root, "Status") ?? string.Empty,
        ConfigFiles = String(root, "ConfigFiles")
    });

    private static List<T> Read<T>(string raw, Func<JsonElement, T> parse)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return [];
        if (trimmed.StartsWith('['))
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.EnumerateArray().Select(parse).ToList();
        }
        return trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return parse(document.RootElement);
        }).ToList();
    }

    private static string? String(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind != JsonValueKind.String) throw new JsonException($"Invalid {name} string.");
        return property.GetString();
    }

    private static int? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property)) return null;
        if (!property.TryGetInt32(out var value)) throw new JsonException($"Invalid {name} integer.");
        return value;
    }
}
