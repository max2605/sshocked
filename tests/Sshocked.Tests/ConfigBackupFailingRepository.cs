using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class ConfigBackupFailingRepository(IConfigRepository repository) : IConfigRepository
{
    public AppConfig Load() => repository.Load();
    public void Save(AppConfig config) => throw new IOException("Simulated configuration write interruption.");
    public void Update(Action<AppConfig> update) => repository.Update(config =>
    { update(config); throw new IOException("Simulated configuration write interruption."); });
}
