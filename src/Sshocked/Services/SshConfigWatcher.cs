using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Sshocked.Interfaces;

[assembly: InternalsVisibleTo("Sshocked.Tests")]

namespace Sshocked.Services;

/// <summary>Debounces editor writes, including atomic-save renames, and never interacts with the terminal.</summary>
public sealed class SshConfigWatcher(ILogger<SshConfigWatcher> logger, TimeProvider? timeProvider = null) : ISshConfigWatcher
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(500);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private ITimer? _timer;
    private long _lastChange;
    private int _generation;
    private bool _pending;
    public event EventHandler? Changed;
    public bool IsWatching { get { lock (_gate) return _watchers.Count > 0; } }

    public void StartWatching(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            StopWatching();
            var generation = _generation;
            _timer = _clock.CreateTimer(_ => Notify(generation), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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
                    OnChange(watcher, null!);
                };
                _watchers.Add(watcher);
                watcher.EnableRaisingEvents = true;
            }
        }
    }

    private void OnChange(object sender, FileSystemEventArgs args)
    {
        lock (_gate)
        {
            if (sender is not FileSystemWatcher watcher || !_watchers.Contains(watcher)) return;
            ScheduleNotification();
        }
    }

    internal void ScheduleNotification()
    {
        lock (_gate)
        {
            if (_timer is null) return;
            _pending = true;
            _lastChange = _clock.GetTimestamp();
            _timer.Change(QuietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    private void Notify(int generation)
    {
        lock (_gate)
        {
            if (_timer is null || generation != _generation || !_pending) return;
            var remaining = QuietPeriod - _clock.GetElapsedTime(_lastChange);
            if (remaining > TimeSpan.Zero)
            {
                // Change cannot retract a callback already queued on the thread pool.
                // Recheck the latest deadline so an old callback cannot announce a new burst early.
                _timer.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }
            _pending = false;
        }
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { logger.LogError(ex, "SSH config change handler failed"); }
    }

    public void StopWatching()
    {
        lock (_gate)
        {
            _generation++;
            _pending = false;
            _timer?.Dispose();
            _timer = null;
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
        }
    }

    public void Dispose() => StopWatching();
}
