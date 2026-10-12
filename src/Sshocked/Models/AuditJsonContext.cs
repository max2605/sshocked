using System.Text.Json.Serialization;

namespace Sshocked.Models;

[JsonSerializable(typeof(AuditEntry))]
[JsonSerializable(typeof(List<AuditEntry>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AuditJsonContext : JsonSerializerContext { }
