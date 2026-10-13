using System.Collections.Concurrent;
using System.Net.Sockets;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

/// <summary>TCP reachability only; an open port does not imply a successful SSH login.</summary>
public sealed class HostReachabilityService(TimeProvider? timeProvider = null) : IHostReachabilityService, IDisposable
{
    private readonly ConcurrentDictionary<string, HostReachabilityServiceEntry> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _concurrency = new(4);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private bool _disposed;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public HostReachability GetStatus(ServerHost host, bool scheduleCheck = false)
    {
        if (Route(host) is { } route) return route;
        if (!IsValidEndpoint(host)) return new(HostReachabilityState.Unchecked);
        var entry = _cache.GetOrAdd(Key(host), _ => new());
        lock (entry.Gate)
        {
            var fresh = entry.LastChecked is not null && _clock.GetUtcNow() - entry.LastChecked < CacheDuration;
            if (scheduleCheck && !fresh && !entry.Checking && !_disposed)
            {
                entry.Checking = true;
                var snapshot = new ServerHost { Id = host.Id, HostName = host.HostName, Port = host.Port };
                // Scheduling on the pool keeps DNS/connect setup off the rendering thread.
                _ = Task.Run(async () =>
                {
                    try { await CheckPortOpen(snapshot, ct: _lifetime.Token); }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { }
                    finally { lock (entry.Gate) entry.Checking = false; }
                });
            }
            var state = entry.Checking ? HostReachabilityState.Checking
                : fresh ? (entry.Reachable == true ? HostReachabilityState.Online : HostReachabilityState.Offline)
                : entry.LastChecked is not null ? HostReachabilityState.Stale
                : HostReachabilityState.Unchecked;
            return new(state, entry.LastChecked, entry.Reachable);
        }
    }

    public async Task<HostReachability> CheckPortOpen(ServerHost host, int timeoutMs = 1000, CancellationToken ct = default)
    {
        if (Route(host) is { } route) return route;
        if (!IsValidEndpoint(host)) return new(HostReachabilityState.Unchecked);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _concurrency.WaitAsync(ct);
        var reachable = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Math.Clamp(timeoutMs, 50, 10000));
            using var client = new TcpClient();
            await client.ConnectAsync(host.HostName, host.Port, timeout.Token);
            reachable = true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ArgumentException) { }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        finally { _concurrency.Release(); }
        var checkedAt = _clock.GetUtcNow();
        var entry = _cache.GetOrAdd(Key(host), _ => new());
        lock (entry.Gate) { entry.LastChecked = checkedAt; entry.Reachable = reachable; }
        return new(reachable ? HostReachabilityState.Online : HostReachabilityState.Offline, checkedAt, reachable);
    }

    private static string Key(ServerHost host) => host.Id + "\0" + host.HostName + "\0" + host.Port;
    private static bool IsValidEndpoint(ServerHost host) => host.Port is >= 1 and <= 65535 && !string.IsNullOrWhiteSpace(host.HostName);
    private static HostReachability? Route(ServerHost host)
        => host.AzureBastion is not null || host.AuthType == AuthType.AzureBastion ? new(HostReachabilityState.ViaBastion)
            : !string.IsNullOrWhiteSpace(host.ProxyJump) && !host.ProxyJump.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? new(HostReachabilityState.ViaJumpHost) : null;

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        // Pending workers own the semaphore/CTS until they have finished; no disposed-object race.
    }
}
