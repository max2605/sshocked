using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SshConfigSyncService(IConfigRepository repository, ISshConfigImporter importer,
    ISshConfigExporter exporter, ISshConfigWatcher watcher, ILogger<SshConfigSyncService> logger) : ISshConfigSyncService
{
    private readonly object _gate = new();
    private bool _started;
    private bool _updating;
    private bool _disposed;
    private bool _pending;
    private string? _lastError;
    private string? _lastWrittenFingerprint;
    private string? _lastHostFingerprint;

    public void Initialize()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            repository.Saved += OnRepositorySaved;
            watcher.Changed += OnSshConfigChanged;
            var config = repository.Load();
            _lastHostFingerprint = HostFingerprint(config.Hosts);
            RefreshMonitoring();
            if (config.SshConfigSync.ImportOnStartup)
                RunBackground(() => Import());
        }
    }

    public void RefreshMonitoring()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var settings = repository.Load().SshConfigSync;
            if (settings.AutoImportOnChange) watcher.StartWatching(WatchedPaths(settings));
            else watcher.StopWatching();
        }
    }

    public SshConfigSyncResult Import(SyncConflictResolution? resolution = null,
        Func<ServerHost, ServerHost, SyncConflictResolution>? resolveConflict = null)
    {
        lock (_gate)
        {
            var incoming = importer.Import();
            var config = repository.Load();
            var policy = resolution ?? config.SshConfigSync.ConflictResolution;
            var decisions = new Dictionary<string, SyncConflictResolution>(StringComparer.OrdinalIgnoreCase);
            // Resolve prompts before acquiring the repository write lock.
            foreach (var host in incoming)
            {
                var match = FindMatch(config.Hosts, host);
                if (match is not null && !SameConnection(match, host))
                    decisions[host.Alias] = policy == SyncConflictResolution.PromptUser
                        ? resolveConflict?.Invoke(match, host) ?? SyncConflictResolution.Skip : policy;
            }
            SshConfigSyncResult result = new(0, 0, 0, 0);
            _updating = true;
            try
            {
                repository.Update(current =>
                {
                    // An unexpected conflict from concurrent editing is skipped, never silently overwritten.
                    result = Merge(current, incoming, SyncConflictResolution.Skip,
                        (existing, imported) =>
                        {
                            var original = FindMatch(config.Hosts, imported);
                            if (original is null || !SameConnection(existing, original)) return SyncConflictResolution.Skip;
                            return decisions.GetValueOrDefault(imported.Alias, SyncConflictResolution.Skip);
                        });
                    current.SshConfigSync.LastImportUtc = DateTimeOffset.UtcNow;
                    current.SshConfigSync.LastImportCount = result.Imported;
                });
            }
            finally { _updating = false; }
            _pending = result.Skipped > 0;
            _lastHostFingerprint = HostFingerprint(repository.Load().Hosts);
            _lastError = null;
            logger.LogInformation("SSH config import: {Added} added, {Updated} updated, {Skipped} skipped", result.Added, result.Updated, result.Skipped);
            return result;
        }
    }

    public SshConfigExportResult Export()
    {
        lock (_gate)
        {
            var config = repository.Load();
            var result = exporter.Export(config.Hosts, config.SshConfigSync);
            _lastWrittenFingerprint = FileFingerprint(config.SshConfigSync);
            _updating = true;
            try
            {
                repository.Update(current =>
                {
                    current.SshConfigSync.LastExportUtc = DateTimeOffset.UtcNow;
                    current.SshConfigSync.LastExportCount = result.Exported;
                });
            }
            finally { _updating = false; }
            _lastHostFingerprint = HostFingerprint(config.Hosts);
            _lastError = null;
            logger.LogInformation("Exported {Count} SSH config host(s), {Changed} changed", result.Exported, result.Changed);
            return result;
        }
    }

    public SshConfigSyncStatus GetStatus()
    {
        lock (_gate)
        {
            var config = repository.Load();
            var openSshCount = 0;
            var differences = false;
            try
            {
                var imported = importer.Import();
                openSshCount = imported.Count;
                differences = imported.Any(host => FindMatch(config.Hosts, host) is not { } existing || !SameConnection(existing, host));
                var selected = config.Hosts.Where(host => !config.SshConfigSync.ExcludeHostIds.Contains(host.Id, StringComparer.Ordinal) &&
                    (config.SshConfigSync.ExportAllHosts || host.Tags.Any(tag => config.SshConfigSync.ExportTags.Contains(tag, StringComparer.OrdinalIgnoreCase))));
                differences |= selected.Any(host => !imported.Any(other => SameConnection(host, other)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { _lastError = ex.Message; }
            return new(watcher.IsWatching, _pending || differences || _lastError is not null, _lastError, config.Hosts.Count, openSshCount, config.SshConfigSync);
        }
    }

    private void OnRepositorySaved(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_updating || _disposed) return;
            var config = repository.Load();
            var fingerprint = HostFingerprint(config.Hosts);
            if (config.SshConfigSync.AutoExportOnChange && fingerprint != _lastHostFingerprint)
                RunBackground(() => Export());
            _lastHostFingerprint = fingerprint;
        }
    }

    private void OnSshConfigChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var config = repository.Load();
            if (!config.SshConfigSync.AutoImportOnChange) return;
            RunBackground(() =>
            {
                var fingerprint = FileFingerprint(config.SshConfigSync);
                if (fingerprint == _lastWrittenFingerprint) return;
                // Default PromptUser never launches UI on a watcher thread. Conflicts become pending changes.
                var result = Import();
                _lastWrittenFingerprint = fingerprint;
                _pending = result.Skipped > 0;
            });
        }
    }

    private void RunBackground(Action operation)
    {
        try { operation(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
        {
            _lastError = ex.Message;
            _pending = true;
            logger.LogWarning(ex, "SSH config synchronization requires attention");
        }
    }

    private static IEnumerable<string> WatchedPaths(SshConfigSyncSettings settings)
    {
        yield return SshConfigExporter.DefaultConfigPath;
        if (settings.Strategy == ExportStrategy.IncludeFile) yield return SshConfigExporter.ResolveIncludePath(settings.IncludeFilePath);
    }

    private static string FileFingerprint(SshConfigSyncSettings settings)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in WatchedPaths(settings))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData(File.Exists(path) ? File.ReadAllBytes(path) : [0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string HostFingerprint(IEnumerable<ServerHost> hosts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var host in hosts.OrderBy(host => host.Id, StringComparer.Ordinal))
            hash.AppendData(Encoding.UTF8.GetBytes(string.Join('\0', host.Id, host.Alias, host.HostName, host.User,
                host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), host.IdentityFile, host.CustomSshOptions,
                host.ProxyJump, host.AuthType.ToString(), string.Join('\0', host.Tags))));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static SshConfigSyncResult Merge(AppConfig config, IEnumerable<ServerHost> incoming, SyncConflictResolution policy,
        Func<ServerHost, ServerHost, SyncConflictResolution>? resolver = null)
    {
        var added = 0; var updated = 0; var skipped = 0; var conflicts = 0;
        foreach (var host in incoming)
        {
            var existing = FindMatch(config.Hosts, host);
            if (existing is null)
            {
                if (config.Hosts.Any(h => h.Id == host.Id)) host.Id = Guid.NewGuid().ToString("N");
                config.Hosts.Add(host);
                added++;
                continue;
            }
            if (SameConnection(existing, host)) continue;
            conflicts++;
            var resolution = resolver?.Invoke(existing, host) ?? policy;
            switch (resolution)
            {
                case SyncConflictResolution.OpenSshWins:
                    CopyConnection(host, existing);
                    updated++;
                    break;
                case SyncConflictResolution.KeepBoth:
                    var prefix = host.Alias + "-openssh";
                    // Repeated automatic imports must not create another copy of the same conflict.
                    if (config.Hosts.Any(h => (h.Alias.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                        h.Alias.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)) && SameEndpoint(h, host))) break;
                    var alias = prefix;
                    var suffix = 2;
                    while (config.Hosts.Any(h => h.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase))) alias = prefix + "-" + suffix++;
                    host.Alias = alias;
                    host.Id = Guid.NewGuid().ToString("N");
                    config.Hosts.Add(host);
                    added++;
                    break;
                case SyncConflictResolution.SshockedWins:
                    break;
                default:
                    skipped++;
                    break;
            }
        }
        return new(added, updated, skipped, conflicts);
    }

    private static ServerHost? FindMatch(IEnumerable<ServerHost> hosts, ServerHost incoming)
    {
        var list = hosts.ToList();
        var exact = list.FirstOrDefault(h => h.Alias.Equals(incoming.Alias, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(h => h.Id == incoming.Id);
        if (exact is not null) return exact;
        var fuzzy = list.Where(h => h.HostName.Equals(incoming.HostName, StringComparison.OrdinalIgnoreCase) &&
            h.User.Equals(incoming.User, StringComparison.Ordinal)).Take(2).ToList();
        // Ambiguous endpoint matches are new hosts, rather than an arbitrary overwritten host.
        return fuzzy.Count == 1 ? fuzzy[0] : null;
    }

    private static bool SameConnection(ServerHost a, ServerHost b)
        => a.Alias.Equals(b.Alias, StringComparison.OrdinalIgnoreCase) && SameEndpoint(a, b);

    private static bool SameEndpoint(ServerHost a, ServerHost b)
        => a.HostName == b.HostName && a.User == b.User && a.Port == b.Port && a.IdentityFile == b.IdentityFile &&
           NormalizeOptions(a.CustomSshOptions) == NormalizeOptions(b.CustomSshOptions) && a.ProxyJump == b.ProxyJump;

    private static string NormalizeOptions(string? options)
    {
        if (string.IsNullOrWhiteSpace(options)) return "";
        try
        {
            return string.Join('\n', SshConfigExporter.ParseOptions(options).Select(option =>
            {
                var parsed = SshConfigSyntax.ParseDirective(option.Key + "=" + option.Value, comments: false)!;
                return parsed.Keyword + "=" + parsed.Value;
            }));
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException) { return options.Trim(); }
    }

    private static void CopyConnection(ServerHost source, ServerHost target)
    {
        target.Alias = source.Alias;
        target.HostName = source.HostName;
        target.User = source.User;
        target.Port = source.Port;
        target.IdentityFile = source.IdentityFile;
        target.CustomSshOptions = source.CustomSshOptions;
        target.ProxyJump = source.ProxyJump;
        // OpenSSH does not encode sshocked's password/agent/secret-provider selection.
        if (source.IdentityFile is not null || target.AuthType == AuthType.CustomConfig)
            target.AuthType = source.AuthType;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            repository.Saved -= OnRepositorySaved;
            watcher.Changed -= OnSshConfigChanged;
            watcher.StopWatching();
        }
    }
}
