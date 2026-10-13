using System.Text.Json;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

internal sealed class ConfigBackupTestFixture : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-backup-tests-" + Guid.NewGuid().ToString("N"));
    public JsonConfigRepository Repository { get; }
    public MacroTestDependencies Audit { get; } = new();
    public ConfigBackupService Backups { get; }
    public string RecoveryDirectory => Path.Combine(directory, "recovery");
    public string PathFor(string name) => Path.Combine(directory, name);

    public ConfigBackupTestFixture()
    {
        Repository = new JsonConfigRepository(PathFor("appconfig.json"));
        Backups = new ConfigBackupService(Repository, Audit, RecoveryDirectory);
    }

    public string Input(AppConfig config, string name = "incoming.json")
    {
        var path = PathFor(name);
        File.WriteAllText(path, JsonSerializer.Serialize(new ConfigBackupDocument { Configuration = config }, ConfigBackupJsonContext.Default.ConfigBackupDocument));
        return path;
    }

    public static ServerHost Host(string id, string alias) => new() { Id = id, Alias = alias, HostName = alias + ".example", User = "operator" };

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
