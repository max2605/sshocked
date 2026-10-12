using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;

namespace Sshocked.Services;

/// <summary>Debounces editor writes, including atomic-save renames, and never interacts with the terminal.</summary>
public sealed class SshConfigWatcher(ILogger<SshConfigWatcher> logger) : ISshConfigWatcher
{
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _timer;
    public event EventHandler? Changed;
    public bool IsWatching { get { lock (_gate) return _watchers.Count > 0; } }

    public void StartWatching(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            StopWatching();
            _timer = new Timer(_ => Notify(), null, Timeout.Infinite, Timeout.Infinite);
            foreach (var path in paths.Select(Path.GetFullPath).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);
                var watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = false
                };
                watcher.Changed += OnChange;
                watcher.Created += OnChange;
                watcher.Deleted += OnChange;
                watcher.Renamed += OnChange;
                watcher.Error += (_, args) =>
                {
                    logger.LogWarning(args.GetException(), "SSH config watcher missed events; scheduling a refresh");
                    OnChange(this, null!);
                };
                _watchers.Add(watcher);
                watcher.EnableRaisingEvents = true;
            }
        }
    }

    private void OnChange(object sender, FileSystemEventArgs args)
    {
        lock (_gate) _timer?.Change(500, Timeout.Infinite);
    }

    private void Notify()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { logger.LogError(ex, "SSH config change handler failed"); }
    }

    public void StopWatching()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
        }
    }

    public void Dispose() => StopWatching();
}
