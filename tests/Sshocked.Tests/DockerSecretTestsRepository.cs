using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class DockerSecretTestsRepository(ServerHost host) : IConfigRepository
{
    public AppConfig Config { get; } = new()
    {
        Hosts = [host]
    };

    public AppConfig Load() => Config;
    public void Save(AppConfig config)
    {
    }
}
