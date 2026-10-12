using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SecretProviderTestsFakeRepository : IConfigRepository
{
    public AppConfig Config { get; } = new();
    public int Saves { get; private set; }

    public AppConfig Load() => Config;
    public void Save(AppConfig config) => Saves++;
}
