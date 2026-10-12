using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class JsonConfigRepository : IConfigRepository
{
    private readonly string _filePath;
    private readonly object _gate = new();
    public event EventHandler? Saved;

    public JsonConfigRepository() : this(Path.Combine(ConfigPaths.DirectoryPath, "appconfig.json")) { }

    public JsonConfigRepository(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public AppConfig Load()
    {
        lock (_gate) return LoadCore();
    }

    private AppConfig LoadCore()
    {
        if (!File.Exists(_filePath))
        {
            var config = new AppConfig { IsFirstStart = true };
            SaveCore(config);
            return config;
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize(json, AppConfigContext.Default.AppConfig)
               ?? throw new InvalidDataException("Configuration contains no object. Restore a valid appconfig.json.");
    }

    public void Save(AppConfig config)
    {
        lock (_gate) SaveCore(config);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppConfig> update)
    {
        lock (_gate)
        {
            var config = LoadCore();
            update(config);
            SaveCore(config);
        }
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void SaveCore(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, AppConfigContext.Default.AppConfig);
        var temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
