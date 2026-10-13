using System.Text.Json;
using System.Text.Json.Nodes;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class ConfigBackupSanitizer
{
    public static ConfigBackupDocument Create(AppConfig source)
    {
        var credentials = source.Hosts.SelectMany(host => new[] { host.SshPassword, host.SudoPassword })
            .Where(value => !string.IsNullOrEmpty(value)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var node = JsonSerializer.SerializeToNode(source, ConfigBackupJsonContext.Default.AppConfig)!;
        var redacted = Clean(node, credentials);
        return new ConfigBackupDocument
        {
            Configuration = node.Deserialize(ConfigBackupJsonContext.Default.AppConfig) ?? throw new InvalidDataException("Configuration is empty."),
            RedactedValues = redacted
        };
    }

    private static int Clean(JsonNode node, IReadOnlyList<string> credentials, bool executableText = false)
    {
        var redacted = 0;
        if (node is JsonObject properties)
        {
            foreach (var pair in properties.ToList())
            {
                if (pair.Key.Equals("environment", StringComparison.OrdinalIgnoreCase) && pair.Value is JsonObject environment)
                {
                    redacted += environment.Count;
                    properties[pair.Key] = new JsonObject();
                    if (environment.Count > 0 && properties.ContainsKey("enabled")) properties["enabled"] = false;
                }
                else if (pair.Value is not null) redacted += CleanValue(properties, pair.Key, pair.Value, credentials,
                    executableText || pair.Key is "command" or "inlineScript" or "arguments" or "customSshOptions" or "commands" or "options");
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var safe = Sanitize(text, credentials, executableText);
                    if (safe != text) { array[index] = safe; redacted++; }
                }
                else if (array[index] is { } child) redacted += Clean(child, credentials, executableText);
            }
        }
        return redacted;
    }

    private static int CleanValue(JsonObject parent, string key, JsonNode node, IReadOnlyList<string> credentials, bool executableText)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text)) return Clean(node, credentials, executableText);
        var safe = Sanitize(text, credentials, executableText);
        if (safe == text) return 0;
        parent[key] = safe;
        if (parent.ContainsKey("enabled") && (key is "command" or "inlineScript" or "arguments")) parent["enabled"] = false;
        return 1;
    }

    private static string Sanitize(string text, IReadOnlyList<string> credentials, bool executableText)
    {
        if (text.Contains("PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase)) return "[private key omitted]";
        foreach (var credential in credentials) text = text.Replace(credential, "[REDACTED]", StringComparison.Ordinal);
        return executableText ? AuditRedactor.Redact(text) ?? string.Empty : text;
    }
}
