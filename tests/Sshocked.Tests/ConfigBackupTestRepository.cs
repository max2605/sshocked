using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class ConfigBackupTestRepository(AppConfig config) : IConfigRepository
{
    public AppConfig Load() => config;
    public void Save(AppConfig replacement) => throw new InvalidOperationException("Unexpected save during backup export.");
}
