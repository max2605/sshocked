using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

/// <summary>Append-only JSONL audit trail with independent retention and privacy settings.</summary>
public sealed partial class AuditService : IAuditService, IDisposable
{
    private readonly IConfigRepository _repository;
    private readonly ILogger<AuditService> _logger;
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, string> _hosts;
    private Dictionary<string, string> _groups;

    public AuditService(IConfigRepository repository, ILogger<AuditService> logger)
        : this(repository, logger, Path.Combine(ConfigPaths.DirectoryPath, "audit", "audit.jsonl")) { }

    public AuditService(IConfigRepository repository, ILogger<AuditService> logger, string path)
    {
        _repository = repository;
        _logger = logger;
        _path = Path.GetFullPath(path);
        var config = repository.Load();
        _hosts = HostSnapshots(config);
        _groups = GroupSnapshots(config);
        repository.Saved += ConfigurationSaved;
    }

    public void Record(AuditEntry entry)
    {
        var settings = _repository.Load().Audit;
        if (!settings.Enabled || !CategoryEnabled(settings, entry.Category)) return;
        entry.Timestamp = entry.Timestamp.ToUniversalTime();
        entry.Username = settings.IncludeLocalUsername ? Environment.UserName : null;
        entry.Hostname = settings.IncludeLocalHostname ? Environment.MachineName : null;
        entry.Action = entry.Category switch
        {
            "command" or "docker" => settings.LogCommandText ? Redact(entry.Action) : "[command omitted]",
            "filetransfer" => settings.LogFilePaths ? Redact(entry.Action) : "[paths omitted]",
            _ => Redact(entry.Action)
        };
        entry.ErrorMessage = Redact(entry.ErrorMessage);
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
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                ApplyRetention(settings);
            }
        }
        catch (IOException ex) { _logger.LogError(ex, "Could not write audit event {EventType}", entry.EventType); }
        catch (UnauthorizedAccessException ex) { _logger.LogError(ex, "Audit directory is not writable"); }
        catch (TimeoutException ex) { _logger.LogError(ex, "Audit file is busy"); }
    }

    public IReadOnlyList<AuditEntry> Query(AuditQuery query)
    {
        var settings = _repository.Load().Audit;
        lock (_gate)
        {
            using var fileLock = CrossProcessFileLock.Acquire(_path);
            if (File.Exists(_path)) ApplyRetention(settings);
            return ReadEntries().Where(e =>
                    (query.Type is null || e.Category.Equals(query.Type, StringComparison.OrdinalIgnoreCase) || e.EventType.Equals(query.Type, StringComparison.OrdinalIgnoreCase)) &&
                    (query.Server is null || string.Equals(e.HostId, query.Server, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Alias, query.Server, StringComparison.OrdinalIgnoreCase)) &&
                    (!query.Since.HasValue || e.Timestamp >= query.Since) && (!query.Until.HasValue || e.Timestamp <= query.Until))
                .OrderByDescending(e => e.Timestamp).Take(Math.Clamp(query.Last, 1, 100000)).ToList();
        }
    }

    private List<AuditEntry> ReadEntries()
    {
        var result = new List<AuditEntry>();
        if (!File.Exists(_path)) return result;
        foreach (var line in File.ReadLines(_path))
        {
            try
            {
                var entry = JsonSerializer.Deserialize(line, AuditJsonContext.Default.AuditEntry);
                if (entry is not null) result.Add(entry);
            }
            catch (JsonException) { _logger.LogWarning("Skipping a malformed audit entry"); }
        }
        return result;
    }

    private void ApplyRetention(AuditSettings settings)
    {
        var entries = ReadEntries();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, settings.RetentionDays));
        var keep = entries.Where(e => e.Timestamp >= cutoff).TakeLast(Math.Max(1, settings.MaxEntries)).ToList();
        if (keep.Count == entries.Count) return;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temporary, keep.Select(e => JsonSerializer.Serialize(e, AuditJsonContext.Default.AuditEntry)));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Clear() { lock (_gate) { using var fileLock = CrossProcessFileLock.Acquire(_path); if (File.Exists(_path)) File.Delete(_path); } }

    private void ConfigurationSaved(object? sender, EventArgs args)
    {
        var config = _repository.Load();
        var hosts = HostSnapshots(config);
        var groups = GroupSnapshots(config);
        List<AuditEntry> entries;
        lock (_gate)
        {
            entries = Compare(_hosts, hosts, "server").Concat(Compare(_groups, groups, "group")).ToList();
            _hosts = hosts;
            _groups = groups;
        }
        foreach (var entry in entries) Record(entry);
    }

    private static IEnumerable<AuditEntry> Compare(Dictionary<string, string> before, Dictionary<string, string> after, string kind)
    {
        foreach (var id in before.Keys.Union(after.Keys))
        {
            var existed = before.TryGetValue(id, out var oldValue);
            var exists = after.TryGetValue(id, out var newValue);
            if (oldValue == newValue) continue;
            var operation = !existed ? "create" : !exists ? "delete" : "update";
            yield return new AuditEntry { Category = "config", EventType = "config." + operation, HostId = id, Action = $"{operation} {kind}" };
        }
    }

    private static Dictionary<string, string> HostSnapshots(AppConfig config) => config.Hosts.GroupBy(h => h.Id)
        .ToDictionary(g => g.Key, g => JsonSerializer.Serialize(g.First(), AppConfigContext.Default.ServerHost));
    private static Dictionary<string, string> GroupSnapshots(AppConfig config) => config.Groups.GroupBy(g => g.Id)
        .ToDictionary(g => g.Key, g => JsonSerializer.Serialize(g.First(), AppConfigContext.Default.ServerGroup));

    private static bool CategoryEnabled(AuditSettings settings, string category) => category switch
    {
        "connection" => settings.LogConnections,
        "command" or "docker" => settings.LogCommands,
        "filetransfer" => settings.LogFileTransfers,
        "config" => settings.LogConfigChanges,
        "secret" => settings.LogSecretAccess,
        _ => true
    };

    public static string? Redact(string? value)
    {
        if (value is null) return null;
        value = SudoInput().Replace(value, "$1[REDACTED]$2");
        value = MysqlPassword().Replace(value, "$1[REDACTED]");
        value = BearerToken().Replace(value, "$1[REDACTED]");
        return SensitiveFlags().Replace(value, match => match.Groups[1].Value + "[REDACTED]");
    }

    [GeneratedRegex("(?i)(echo\\s+)(?:\"[^\"]*\"|'[^']*'|[^\\s|]+)(\\s*\\|\\s*sudo\\s+-S)")]
    private static partial Regex SudoInput();
    [GeneratedRegex("(?i)(\\bmysql\\b[^\\r\\n]*?\\s-p)(?:\"[^\"]*\"|'[^']*'|[^\\s;]+)")]
    private static partial Regex MysqlPassword();
    [GeneratedRegex("(?i)(Bearer\\s+)[A-Za-z0-9._~+/-]+={0,2}")]
    private static partial Regex BearerToken();

    [GeneratedRegex("(?i)((?:--?(?:password|passwd|passphrase|token|secret|api-key)|(?:password|passwd|token|secret|api_key))\\s*(?:=|:)??\\s*)(?:\"[^\"]*\"|'[^']*'|[^\\s;]+)")]
    private static partial Regex SensitiveFlags();

    public void Dispose() => _repository.Saved -= ConfigurationSaved;
}
