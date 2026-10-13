using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class DiagnosticTestDependencies : IConfigRepository, IDiagnosticToolService, IHostReachabilityService,
    IHostResolutionService, IAuditService, IConsoleWriterService
{
    public AppConfig Config { get; set; } = new();
    public List<DiagnosticTool> Tools { get; set; } = [new("ssh", true, "OpenSSH_9.8", null), new("wt", false, null, null)];
    public List<AuditEntry> Entries { get; } = [];
    public List<string> Output { get; } = [];
    public int ProbeCalls { get; private set; }
    public string? QueriedHost { get; private set; }
    public AppConfig Load() => Config;
    public void Save(AppConfig config) => Config = config;
    public Task<IReadOnlyList<DiagnosticTool>> Inspect(bool includePaths, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DiagnosticTool>>(Tools);
    public HostReachability GetStatus(ServerHost host, bool scheduleCheck = false) => new(HostReachabilityState.Unchecked);
    public Task<HostReachability> CheckPortOpen(ServerHost host, int timeoutMs = 1000, CancellationToken ct = default)
    { ProbeCalls++; return Task.FromResult(new HostReachability(HostReachabilityState.Offline, DateTimeOffset.UtcNow, false)); }
    public HostResolution GetResolution(string hostName, bool scheduleCheck = false) => new(HostResolutionState.Resolved, ["192.0.2.10"]);
    public long Revision => 0;
    public bool IsRefreshDue(IEnumerable<string> hostNames) => false;
    public void Record(AuditEntry entry) => Entries.Add(entry);
    public IReadOnlyList<AuditEntry> Query(AuditQuery query)
    {
        QueriedHost = query.Server;
        return Entries.Where(entry => query.Server is null || entry.HostId == query.Server).Take(query.Last).ToList();
    }
    public void Clear() => Entries.Clear();
    public void WriteLine(string message) => Output.Add(message);
    public void Write(string message) => Output.Add(message);
    public string? ReadLine() => throw new InvalidOperationException("Diagnostics must not read console input.");
}
