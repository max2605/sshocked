using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MonitoringStore(string? directory = null) : IMonitoringStore
{
    private readonly string _directory = directory ?? Path.Combine(ConfigPaths.DirectoryPath, "monitoring");

    private string PathFor(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Monitor ID is required.");
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
    }

    public MonitoringRecord? Load(string monitorId)
    {
        var path = PathFor(monitorId);
        using var gate = CrossProcessFileLock.Acquire(path);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length > 16777216) throw new InvalidDataException("Monitoring state exceeds its size limit.");
        return JsonSerializer.Deserialize(File.ReadAllText(path), MonitoringJsonContext.Default.MonitoringRecord)
            ?? throw new InvalidDataException("Invalid monitoring state.");
    }

    public void Save(string monitorId, MonitoringRecord record)
    {
        var path = PathFor(monitorId);
        var json = JsonSerializer.Serialize(record, MonitoringJsonContext.Default.MonitoringRecord);
        if (Encoding.UTF8.GetByteCount(json) > 16777216) throw new InvalidDataException("Monitoring state exceeds its size limit.");
        using var gate = CrossProcessFileLock.Acquire(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<IAsyncDisposable> Lease(string monitorId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(monitorId) + ".lease";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new MonitoringFileLease(stream);
            }
            catch (IOException) { await Task.Delay(100, cancellationToken); }
        }
    }

    public void Delete(string monitorId)
    {
        var path = PathFor(monitorId);
        using var gate = CrossProcessFileLock.Acquire(path);
        if (File.Exists(path)) File.Delete(path);
    }
}
