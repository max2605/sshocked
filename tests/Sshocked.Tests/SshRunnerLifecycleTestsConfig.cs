using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SshRunnerLifecycleTestsConfig : IConfigRepository
{
    private AppConfig value = new()
    {
        Groups = [new ServerGroup
        {
            Id = "prod",
            Name = "Production"
        }

        ]
    };
    public AppConfig Load() => value;
    public void Save(AppConfig config) => value = config;
}
