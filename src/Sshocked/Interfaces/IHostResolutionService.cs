using Sshocked.Models;

namespace Sshocked.Interfaces;

/// <summary>Cached local DNS results, independent of an SSH route or TCP connection.</summary>
public interface IHostResolutionService
{
    HostResolution GetResolution(string hostName, bool scheduleCheck = false);
    long Revision { get; }
    bool IsRefreshDue(IEnumerable<string> hostNames);
}
