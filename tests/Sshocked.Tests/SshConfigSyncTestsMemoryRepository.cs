using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SshConfigSyncTestsMemoryRepository : IConfigRepository
{
    public AppConfig Config { get; } = new();

    public event EventHandler? Saved;
    public AppConfig Load() => Config;
    public void Save(AppConfig config) => Saved?.Invoke(this, EventArgs.Empty);
    public void Update(Action<AppConfig> update)
    {
        update(Config);
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
