using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IHostReachabilityService
{
    HostReachability GetStatus(ServerHost host, bool scheduleCheck = false);
    Task<HostReachability> CheckPortOpen(ServerHost host, int timeoutMs = 1000, CancellationToken ct = default);
}
