using System.Security.Cryptography;
using System.Text;

namespace Sshocked.Services;

/// <summary>
/// Serializes synchronous operations on one file across sshocked processes.
/// Acquire and dispose on the same thread; do not await while holding this lock.
/// </summary>
public sealed class CrossProcessFileLock : IDisposable
{
    private Mutex? _mutex;

    private CrossProcessFileLock(Mutex mutex) => _mutex = mutex;

    public static CrossProcessFileLock Acquire(string path, TimeSpan? timeout = null)
    {
        var fullPath = Path.GetFullPath(path);
        var normalized = OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        var name = (OperatingSystem.IsWindows() ? "Local\\" : "") + "sshocked-file-" + pathHash;
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            try
            {
                if (!mutex.WaitOne(timeout ?? TimeSpan.FromSeconds(30)))
                    throw new TimeoutException($"File '{fullPath}' is busy in another sshocked window. Retry shortly.");
            }
            catch (AbandonedMutexException)
            {
                // WaitOne grants ownership when a crashed process abandoned the mutex.
                // File writers must still use atomic replacement or recover incomplete records.
            }
            return new CrossProcessFileLock(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        var mutex = _mutex;
        if (mutex is null) return;
        // Release first so an invalid cross-thread disposal does not silently lose ownership.
        mutex.ReleaseMutex();
        _mutex = null;
        mutex.Dispose();
    }
}
