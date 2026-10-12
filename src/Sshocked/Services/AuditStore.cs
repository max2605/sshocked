using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class AuditStore(IConfigRepository repository, ILogger<AuditService> logger, string path) : IDisposable
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly object _gate = new();
    private AuditConfigurationTracker? _configurationTracker;
    public static AuditStore Create(IConfigRepository repository, ILogger<AuditService> logger, string path)
    {
        var store = new AuditStore(repository, logger, path);
        store._configurationTracker = AuditConfigurationTracker.Subscribe(repository, store.Record);
        return store;
    }

    public void Record(AuditEntry entry)
    {
        var settings = repository.Load().Audit;
        if (!settings.Enabled || !AuditPolicy.CategoryEnabled(settings, entry.Category))
            return;
        entry.Timestamp = entry.Timestamp.ToUniversalTime();
        entry.Username = settings.IncludeLocalUsername ? Environment.UserName : null;
        entry.Hostname = settings.IncludeLocalHostname ? Environment.MachineName : null;
        entry.Action = entry.Category switch
        {
            "command" or "docker" => settings.LogCommandText ? AuditRedactor.Redact(entry.Action) : "[command omitted]",
            "filetransfer" => settings.LogFilePaths ? AuditRedactor.Redact(entry.Action) : "[paths omitted]",
            _ => AuditRedactor.Redact(entry.Action)
        };
        entry.ErrorMessage = AuditRedactor.Redact(entry.ErrorMessage);
        try
        {
            lock (_gate)
            {
                using var fileLock = CrossProcessFileLock.Acquire(_path);
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream))
                {
                    writer.WriteLine(JsonSerializer.Serialize(entry, AuditJsonContext.Default.AuditEntry));
                    writer.Flush();
                    stream.Flush(true);
                }

                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                ApplyRetention(settings);
            }
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Could not write audit event {EventType}", entry.EventType);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Audit directory is not writable");
        }
        catch (TimeoutException ex)
        {
            logger.LogError(ex, "Audit file is busy");
        }
    }

    public IReadOnlyList<AuditEntry> Query(AuditQuery query)
    {
        var settings = repository.Load().Audit;
        lock (_gate)
        {
            using var fileLock = CrossProcessFileLock.Acquire(_path);
            if (File.Exists(_path))
                ApplyRetention(settings);
            return ReadEntries().Where(e => (query.Type is null || e.Category.Equals(query.Type, StringComparison.OrdinalIgnoreCase) || e.EventType.Equals(query.Type, StringComparison.OrdinalIgnoreCase)) && (query.Server is null || string.Equals(e.HostId, query.Server, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Alias, query.Server, StringComparison.OrdinalIgnoreCase)) && (!query.Since.HasValue || e.Timestamp >= query.Since) && (!query.Until.HasValue || e.Timestamp <= query.Until)).OrderByDescending(e => e.Timestamp).Take(Math.Clamp(query.Last, 1, 100000)).ToList();
        }
    }

    private List<AuditEntry> ReadEntries()
    {
        var result = new List<AuditEntry>();
        if (!File.Exists(_path))
            return result;
        foreach (var line in File.ReadLines(_path))
        {
            try
            {
                var entry = JsonSerializer.Deserialize(line, AuditJsonContext.Default.AuditEntry);
                if (entry is not null)
                    result.Add(entry);
            }
            catch (JsonException)
            {
                logger.LogWarning("Skipping a malformed audit entry");
            }
        }

        return result;
    }

    private void ApplyRetention(AuditSettings settings)
    {
        var entries = ReadEntries();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, settings.RetentionDays));
        var keep = entries.Where(e => e.Timestamp >= cutoff).TakeLast(Math.Max(1, settings.MaxEntries)).ToList();
        if (keep.Count == entries.Count)
            return;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temporary, keep.Select(e => JsonSerializer.Serialize(e, AuditJsonContext.Default.AuditEntry)));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            using var fileLock = CrossProcessFileLock.Acquire(_path);
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    public void Dispose() => _configurationTracker?.Dispose();
}
