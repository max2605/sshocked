using System.Text.Json.Serialization;

namespace Sshocked.Models;

public sealed class ServerHost
{
    public string Id { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string GroupId { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public AuthType AuthType { get; set; } = AuthType.SshKey;
    public string? IdentityFile { get; set; }
    public string? CustomSshOptions { get; set; }
    public DockerHostInfo? DockerInfo { get; set; }
    public List<ContainerModel> SavedContainers { get; set; } = [];
    public bool IsFavorite { get; set; }
    public string? ProxyJump { get; set; }
    public AzureBastionConfig? AzureBastion { get; set; }
    public SecretRef? KeySecretRef { get; set; }
    public SecretRef? PasswordSecretRef { get; set; }
    public SecretRef? SudoPasswordSecretRef { get; set; }

    [JsonIgnore]
    public bool UsesAzureBastion => AzureBastion is not null;

    [JsonIgnore]
    public string? SshHostKeyAlias { get; set; }

    [JsonIgnore]
    public string? SudoPassword { get; set; }

    [JsonIgnore]
    public string? SshPassword { get; set; }
}
