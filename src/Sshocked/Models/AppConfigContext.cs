using System.Text.Json.Serialization;

namespace Sshocked.Models;

[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(RecentConnection))]
[JsonSerializable(typeof(AuditEntry))]
[JsonSerializable(typeof(List<AuditEntry>))]
[JsonSerializable(typeof(ServerGroup))]
[JsonSerializable(typeof(ServerHost))]
[JsonSerializable(typeof(AuthType))]
[JsonSerializable(typeof(ConnectionStrategy))]
[JsonSerializable(typeof(DockerHostInfo))]
[JsonSerializable(typeof(ContainerModel))]
[JsonSerializable(typeof(ComposeStackModel))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppConfigContext : JsonSerializerContext
{
}
