using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class TableRendererTestsMemoryRepository(AppConfig config) : IConfigRepository
{
    public AppConfig Load() => config;
    public void Save(AppConfig updated)
    {
    }
}
