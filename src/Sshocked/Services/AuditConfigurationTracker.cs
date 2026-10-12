using System.Text.Json;

using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class AuditConfigurationTracker(IConfigRepository repository, Action<AuditEntry> record) : IDisposable
{
    private readonly object _gate = new();
    private (Dictionary<string, string> Hosts, Dictionary<string, string> Groups) _snapshots = Snapshots(repository.Load());
    private static (Dictionary<string, string>, Dictionary<string, string>) Snapshots(AppConfig config) => (HostSnapshots(config), GroupSnapshots(config));
    public static AuditConfigurationTracker Subscribe(IConfigRepository repository, Action<AuditEntry> record)
    {
        var tracker = new AuditConfigurationTracker(repository, record);
        repository.Saved += tracker.ConfigurationSaved;
        return tracker;
    }

    private void ConfigurationSaved(object? sender, EventArgs args)
    {
        var config = repository.Load();
        var hosts = HostSnapshots(config);
        var groups = GroupSnapshots(config);
        List<AuditEntry> entries;
        lock (_gate)
        {
            entries = Compare(_snapshots.Hosts, hosts, "server").Concat(Compare(_snapshots.Groups, groups, "group")).ToList();
            _snapshots.Hosts = hosts;
            _snapshots.Groups = groups;
        }

        foreach (var entry in entries)
            record(entry);
    }

    private static IEnumerable<AuditEntry> Compare(Dictionary<string, string> before, Dictionary<string, string> after, string kind)
    {
        foreach (var id in before.Keys.Union(after.Keys))
        {
            var existed = before.TryGetValue(id, out var oldValue);
            var exists = after.TryGetValue(id, out var newValue);
            if (oldValue == newValue)
                continue;
            var operation = !existed ? "create" : !exists ? "delete" : "update";
            yield return new AuditEntry
            {
                Category = "config",
                EventType = "config." + operation,
                HostId = id,
                Action = $"{operation} {kind}"
            };
        }
    }

    private static Dictionary<string, string> HostSnapshots(AppConfig config) => config.Hosts.GroupBy(h => h.Id).ToDictionary(g => g.Key, g => JsonSerializer.Serialize(g.First(), AppConfigContext.Default.ServerHost));
    private static Dictionary<string, string> GroupSnapshots(AppConfig config) => config.Groups.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => JsonSerializer.Serialize(g.First(), AppConfigContext.Default.ServerGroup));
    public void Dispose() => repository.Saved -= ConfigurationSaved;
}
