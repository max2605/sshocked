using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class TableRendererTestsFakeResolution : IHostResolutionService
{
    public Dictionary<string, HostResolution> Results { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Requested { get; } = [];
    public List<string> VisibleHostNames { get; } = [];
    public int Scheduled { get; private set; }
    public long Revision { get; set; }
    public bool RefreshDue { get; set; }

    public HostResolution GetResolution(string hostName, bool scheduleCheck = false)
    {
        Requested.Add(hostName);
        if (scheduleCheck) Scheduled++;
        return Results.GetValueOrDefault(hostName) ?? new(HostResolutionState.Resolved, ["192.0.2.42", "2001:db8::42"]);
    }

    public bool IsRefreshDue(IEnumerable<string> hostNames)
    {
        VisibleHostNames.Clear();
        VisibleHostNames.AddRange(hostNames);
        return RefreshDue && VisibleHostNames.Count > 0;
    }
}
