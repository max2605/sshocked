using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

/// <summary>Nonblocking local DNS lookups; resolution does not establish reachability.</summary>
public sealed class HostResolutionService(
    IDnsResolver resolver,
    TimeProvider? timeProvider = null,
    TimeSpan? lookupTimeout = null) : IHostResolutionService, IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _concurrency = new(4);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _lookupTimeout = ClampTimeout(lookupTimeout ?? TimeSpan.FromSeconds(2));
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private long _revision;
    private int _disposed;

    public long Revision => Interlocked.Read(ref _revision);

    public HostResolution GetResolution(string hostName, bool scheduleCheck = false)
    {
        var name = Normalize(hostName);
        if (string.IsNullOrEmpty(name)) return new(HostResolutionState.Error, Error: "Invalid host");
        if (TryLiteral(name, out var literal))
            return new(HostResolutionState.Resolved, [literal.ToString()]);

        var entry = _cache.GetOrAdd(name, _ => new());
        lock (entry.Gate)
        {
            if (scheduleCheck && IsExpired(entry) && !entry.Checking && Volatile.Read(ref _disposed) == 0)
            {
                entry.Checking = true;
                // DNS initialization and resolver callbacks stay off the menu/rendering thread.
                _ = Task.Run(() => ResolveAsync(name, entry));
            }
            if (entry.Result is { } result) return result with { IsRefreshing = entry.Checking };
            return new(entry.Checking ? HostResolutionState.Resolving : HostResolutionState.Unchecked);
        }
    }

    public bool IsRefreshDue(IEnumerable<string> hostNames)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        foreach (var hostName in hostNames)
        {
            if (!_cache.TryGetValue(Normalize(hostName), out var entry)) continue;
            lock (entry.Gate)
            {
                if (entry.Result is not null && !entry.Checking && IsExpired(entry)) return true;
            }
        }
        return false;
    }

    private async Task ResolveAsync(string name, Entry entry)
    {
        var entered = false;
        HostResolution? result = null;
        try
        {
            await _concurrency.WaitAsync(_lifetime.Token);
            entered = true;
            // A cancelled worker may release a permit before queued waits observe cancellation.
            if (Volatile.Read(ref _disposed) != 0) return;
            _lifetime.Token.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(_lookupTimeout);
            // WaitAsync also bounds a resolver that ignores its cancellation token.
            timeout.Token.ThrowIfCancellationRequested();
            var addresses = await resolver.ResolveAsync(name, timeout.Token).WaitAsync(timeout.Token);
            var values = addresses
                .Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ThenBy(address => address.ToString(), StringComparer.Ordinal)
                .Select(address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            result = values.Length > 0
                ? new(HostResolutionState.Resolved, Array.AsReadOnly(values), LastCheckedUtc: _clock.GetUtcNow())
                : Error("No IP addresses");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) { result = Error("DNS timeout"); }
        catch (SocketException exception)
        {
            result = Error(exception.SocketErrorCode switch
            {
                SocketError.HostNotFound => "Name not found",
                SocketError.NoData => "No IP addresses",
                SocketError.TimedOut => "DNS timeout",
                SocketError.TryAgain => "DNS unavailable",
                _ => "DNS error"
            });
        }
        catch (ArgumentException) { result = Error("Invalid host"); }
        catch (Exception) { result = Error("DNS error"); }
        finally
        {
            if (entered) _concurrency.Release();
            lock (entry.Gate)
            {
                if (result is not null && Volatile.Read(ref _disposed) == 0)
                {
                    entry.Result = result;
                    Interlocked.Increment(ref _revision);
                }
                entry.Checking = false;
            }
        }
    }

    private HostResolution Error(string message)
        => new(HostResolutionState.Error, Error: message, LastCheckedUtc: _clock.GetUtcNow());

    private bool IsExpired(Entry entry)
        => entry.Result?.LastCheckedUtc is not { } checkedAt || _clock.GetUtcNow() - checkedAt >= CacheDuration;

    private static string Normalize(string? hostName) => hostName?.Trim() ?? string.Empty;

    private static bool TryLiteral(string name, out IPAddress address)
    {
        if (IPAddress.TryParse(name, out address!)) return true;
        return name.Length > 2 && name[0] == '[' && name[^1] == ']'
            && IPAddress.TryParse(name[1..^1], out address!)
            && address.AddressFamily == AddressFamily.InterNetworkV6;
    }

    private static TimeSpan ClampTimeout(TimeSpan timeout)
        => TimeSpan.FromMilliseconds(Math.Clamp(timeout.TotalMilliseconds, 10, 10000));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        // Workers release their permits and linked token sources after cancellation.
        // Keep shared synchronization objects alive until all workers have returned.
    }

    private sealed class Entry
    {
        public object Gate { get; } = new();
        public HostResolution? Result { get; set; }
        public bool Checking { get; set; }
    }
}
