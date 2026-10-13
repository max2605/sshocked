using System.Text.Json.Serialization;

namespace Sshocked.Models;

[JsonSerializable(typeof(ConfigBackupDocument))]
[JsonSerializable(typeof(ConfigBackupLocalRecoveryDocument))]
[JsonSerializable(typeof(AppConfig))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class ConfigBackupJsonContext : JsonSerializerContext
{
}
