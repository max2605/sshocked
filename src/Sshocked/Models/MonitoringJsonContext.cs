using System.Text.Json.Serialization;

namespace Sshocked.Models;

[JsonSerializable(typeof(MonitoringRecord))]
[JsonSerializable(typeof(List<MonitoringRecord>))]
[JsonSerializable(typeof(MonitorDefinition))]
[JsonSerializable(typeof(List<MonitorDefinition>))]
[JsonSerializable(typeof(MonitorSnapshot))]
[JsonSerializable(typeof(List<MonitorSnapshot>))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class MonitoringJsonContext : JsonSerializerContext
{
}
