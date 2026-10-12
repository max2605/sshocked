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
                var match = SshConfigMerge.FindMatch(config.Hosts, host);
                if (match is not null && !SshConfigMerge.SameConnection(match, host))
                    decisions[host.Alias] = policy == SyncConflictResolution.PromptUser
                        ? resolveConflict?.Invoke(match, host) ?? SyncConflictResolution.Skip : policy;
            }
            var result = new SshConfigSyncResult(0, 0, 0, 0);
            _updating = true;
            try
            {
                repository.Update(current =>
                {
                    // An unexpected conflict from concurrent editing is skipped, never silently overwritten.
                    result = SshConfigMerge.Merge(current, incoming, SyncConflictResolution.Skip,
                        (existing, imported) =>
                        {
                            var original = SshConfigMerge.FindMatch(config.Hosts, imported);
                            if (original is null || !SshConfigMerge.SameConnection(existing, original)) return SyncConflictResolution.Skip;
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
            _pending = false;
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
                differences = imported.Any(host => SshConfigMerge.FindMatch(config.Hosts, host) is not { } existing || !SshConfigMerge.SameConnection(existing, host));
                var selected = config.Hosts.Where(host => !config.SshConfigSync.ExcludeHostIds.Contains(host.Id, StringComparer.Ordinal) &&
                    (config.SshConfigSync.ExportAllHosts || host.Tags.Any(tag => config.SshConfigSync.ExportTags.Contains(tag, StringComparer.OrdinalIgnoreCase))));
                differences |= selected.Any(host => !imported.Any(other => SshConfigMerge.SameConnection(host, other)));
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
