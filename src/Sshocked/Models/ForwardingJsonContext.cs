using System.Text.Json.Serialization;

namespace Sshocked.Models;

[JsonSerializable(typeof(ForwardingProfile))]
[JsonSerializable(typeof(ForwardingStatus))]
[JsonSerializable(typeof(ForwardingRuntimeRecord))]
[JsonSerializable(typeof(List<ForwardingStatus>))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class ForwardingJsonContext : JsonSerializerContext;
