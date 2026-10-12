using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sshocked.Services;

namespace Sshocked.Logging;

public sealed class FileLoggerProcessor : IDisposable
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxBackupFiles = 3;

    private readonly string _logDir;
    private readonly string _logFilePath;
    private readonly BlockingCollection<LogEntry> _queue = new(new ConcurrentQueue<LogEntry>());
    private readonly Thread _workerThread;
    private readonly object _gate = new();
    private bool _disposed;

    public FileLoggerProcessor(string? directory = null)
    {
        _logDir = directory ?? Path.Combine(ConfigPaths.DirectoryPath, "logs");
        Directory.CreateDirectory(_logDir);
        _logFilePath = Path.Combine(_logDir, "sshocked.log");

        _workerThread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "FileLogger"
        };
        _workerThread.Start();
    }

    public void Enqueue(LogEntry entry)
    {
        lock (_gate)
        {
            if (!_disposed) _queue.Add(entry);
        }
    }

    private void ProcessQueue()
    {
        foreach (var entry in _queue.GetConsumingEnumerable())
        {
            try
            {
                using var fileLock = CrossProcessFileLock.Acquire(_logFilePath);
                RollIfNeeded();
                var line = FormatEntry(entry);
                File.AppendAllText(_logFilePath, line);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_logFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // Swallow write errors — logging must never crash the app
            }
        }
    }

    private void RollIfNeeded()
    {
        if (!File.Exists(_logFilePath))
            return;

        var fileInfo = new FileInfo(_logFilePath);
        if (fileInfo.Length < MaxFileSizeBytes)
            return;

        var oldestBackup = Path.Combine(_logDir, $"sshocked.{MaxBackupFiles}.log");
        if (File.Exists(oldestBackup))
            File.Delete(oldestBackup);

        for (int i = MaxBackupFiles - 1; i >= 1; i--)
        {
            var src = Path.Combine(_logDir, $"sshocked.{i}.log");
            var dst = Path.Combine(_logDir, $"sshocked.{i + 1}.log");
            if (File.Exists(src))
                File.Move(src, dst, overwrite: true);
        }

        File.Move(_logFilePath, Path.Combine(_logDir, "sshocked.1.log"), overwrite: true);
    }

    private static string FormatEntry(LogEntry entry)
    {
        var level = entry.LogLevel switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            _ => "UNKN"
        };

        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level}] {entry.CategoryName}: {entry.Message}{Environment.NewLine}";
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CompleteAdding();
        }
        // Drain queued shutdown messages. Leave the collection alive if a slow file
        // operation outlasts the bounded shutdown wait; the worker still owns it.
        if (_workerThread.Join(TimeSpan.FromSeconds(5))) _queue.Dispose();
    }
}
