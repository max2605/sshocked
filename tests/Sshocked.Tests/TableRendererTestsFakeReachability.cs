using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class TableRendererTestsFakeReachability : IHostReachabilityService
{
    public int Scheduled { get; private set; }

    public HostReachability GetStatus(ServerHost host, bool scheduleCheck = false)
    {
        if (scheduleCheck)
            Scheduled++;
        return new(HostReachabilityState.Unchecked);
    }

    public Task<HostReachability> CheckPortOpen(ServerHost host, int timeoutMs = 1000, CancellationToken ct = default) => Task.FromResult(new HostReachability(HostReachabilityState.Unchecked));
}
