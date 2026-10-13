using System.Text.Json.Serialization;

namespace Sshocked.Models;

public sealed class ConfigBackupLocalRecoveryDocument
{
    [JsonRequired]
    public string Format { get; set; } = "sshocked-local-recovery";
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonRequired]
    public string UserBinding { get; set; } = string.Empty;
    [JsonRequired]
    public AppConfig Configuration { get; set; } = new();
}
