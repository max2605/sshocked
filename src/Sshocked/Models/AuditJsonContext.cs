using System.Text.Json.Serialization;

namespace Sshocked.Models;

[JsonSerializable(typeof(AuditEntry))]
[JsonSerializable(typeof(List<AuditEntry>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class AuditJsonContext : JsonSerializerContext { }
