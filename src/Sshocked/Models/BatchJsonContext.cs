using System.Text.Json.Serialization;

namespace Sshocked.Models;

[JsonSerializable(typeof(BatchReport))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
public sealed partial class BatchJsonContext : JsonSerializerContext;
