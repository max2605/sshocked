using System.Text.Json.Serialization;

namespace Sshocked.Models;

[JsonSerializable(typeof(DiagnosticReport))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class DiagnosticJsonContext : JsonSerializerContext
{
}
