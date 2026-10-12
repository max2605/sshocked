using System.Text.Json.Serialization;

namespace Sshocked.Models;

[JsonSerializable(typeof(SessionMetadata))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
public sealed partial class SessionRecordingJsonContext : JsonSerializerContext;
