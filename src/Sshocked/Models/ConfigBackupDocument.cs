using System.Text.Json.Serialization;

namespace Sshocked.Models;

public sealed class ConfigBackupDocument
{
    [JsonRequired]
    public string Format { get; set; } = "sshocked-configuration";
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ApplicationVersion { get; set; } = typeof(ConfigBackupDocument).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    public int RedactedValues { get; set; }
    [JsonRequired]
    public AppConfig Configuration { get; set; } = new();
}
