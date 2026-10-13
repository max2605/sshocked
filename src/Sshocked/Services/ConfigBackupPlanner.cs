using System.Text.Json;
using System.Text.Json.Nodes;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class ConfigBackupPlanner
{
    private static readonly string[] Lists = ["groups", "hosts", "macros", "targetViews", "secretProviders", "forwardingProfiles"];

    public static AppConfig Candidate(AppConfig current, AppConfig incoming, ConfigBackupRestoreMode mode)
    {
        if (mode == ConfigBackupRestoreMode.Replace) return incoming;
        var candidate = Node(current);
        var imported = Node(incoming);
        foreach (var list in Lists) Merge(candidate[list]!.AsArray(), imported[list]!.AsArray(), list == "secretProviders" ? "name" : "id");
        Merge(candidate["monitoring"]!["monitors"]!.AsArray(), imported["monitoring"]!["monitors"]!.AsArray(), "id");
        return candidate.Deserialize(ConfigBackupJsonContext.Default.AppConfig) ?? throw new InvalidDataException("Configuration is empty.");
    }

    public static IReadOnlyList<ConfigBackupChange> Changes(AppConfig current, AppConfig candidate)
    {
        var oldNode = Node(current);
        var newNode = Node(candidate);
        var changes = new List<ConfigBackupChange>();
        foreach (var list in Lists)
        {
            Diff(list, oldNode[list]!.AsArray(), newNode[list]!.AsArray(), list == "secretProviders" ? "name" : "id", changes);
            oldNode.Remove(list); newNode.Remove(list);
        }
        Diff("monitors", oldNode["monitoring"]!["monitors"]!.AsArray(), newNode["monitoring"]!["monitors"]!.AsArray(), "id", changes);
        oldNode["monitoring"]!.AsObject().Remove("monitors"); newNode["monitoring"]!.AsObject().Remove("monitors");
        if (!JsonNode.DeepEquals(oldNode, newNode)) changes.Add(new("settings", "application", "Application settings and policies", "changed"));
        return changes.AsReadOnly();
    }

    private static JsonObject Node(AppConfig config) => JsonSerializer.SerializeToNode(config, ConfigBackupJsonContext.Default.AppConfig)!.AsObject();
    private static string Identity(JsonNode item, string key) => item[key]!.GetValue<string>();
    private static string Name(JsonNode item) => (item["alias"] ?? item["name"] ?? item["id"])!.GetValue<string>();

    private static void Merge(JsonArray current, JsonArray incoming, string key)
    {
        foreach (var item in incoming)
        {
            var id = Identity(item!, key);
            var index = current.Select((value, position) => (value, position)).FirstOrDefault(entry =>
                Identity(entry.value!, key).Equals(id, key == "name" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), (null, -1)).position;
            if (index < 0) current.Add(item!.DeepClone());
            else current[index] = item!.DeepClone();
        }
    }

    private static void Diff(string category, JsonArray current, JsonArray candidate, string key, ICollection<ConfigBackupChange> changes)
    {
        var comparer = key == "name" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var oldItems = current.GroupBy(item => Identity(item!, key), comparer).ToDictionary(group => group.Key, group => group.First()!, comparer);
        var newItems = candidate.GroupBy(item => Identity(item!, key), comparer).ToDictionary(group => group.Key, group => group.First()!, comparer);
        foreach (var pair in newItems)
        {
            if (!oldItems.TryGetValue(pair.Key, out var original)) changes.Add(new(category, pair.Key, Name(pair.Value), "added"));
            else if (!JsonNode.DeepEquals(original, pair.Value)) changes.Add(new(category, pair.Key, Name(pair.Value), "changed"));
        }
        foreach (var pair in oldItems.Where(pair => !newItems.ContainsKey(pair.Key))) changes.Add(new(category, pair.Key, Name(pair.Value), "removed"));
    }
}
