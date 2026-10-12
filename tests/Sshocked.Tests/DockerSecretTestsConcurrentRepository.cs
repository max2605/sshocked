using System.Text.Json;


using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class DockerSecretTestsConcurrentRepository(ServerHost host) : IConfigRepository
{
    public AppConfig Config { get; private set; } = new()
    {
        Hosts = [new ServerHost
        {
            Id = host.Id,
            IdentityFile = host.IdentityFile
        }

        ]
    };

    public AppConfig Load()
    {
        var snapshot = JsonSerializer.Deserialize(JsonSerializer.Serialize(Config, AppConfigContext.Default.AppConfig), AppConfigContext.Default.AppConfig)!;
        ConcurrentEdit();
        return snapshot;
    }

    public void Save(AppConfig config) => Config = config;
    public void Update(Action<AppConfig> update)
    {
        ConcurrentEdit();
        update(Config);
    }

    private void ConcurrentEdit()
    {
        Config.Hosts[0].IdentityFile = "rotated-key";
        Config.Hosts[0].KeySecretRef = new()
        {
            SecretId = "rotated-secret",
            SecretType = SecretType.SshPrivateKey
        };
    }
}
