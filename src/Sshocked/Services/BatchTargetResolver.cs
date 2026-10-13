using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class BatchTargetResolver(IConfigRepository repository, ITargetViewService views)
{
    public IReadOnlyList<ServerHost> Resolve(CommandOptions options)
    {
        if (new[] { "server", "group", "tag", "view" }.Count(options.Has) != 1)
            throw new ArgumentException("Select exactly one --server, --group, --tag or --view.");
        if (options.Get("view") is { } view) return views.Resolve(view);
        var config = repository.Load();
        if (options.Get("server") is { } server)
        {
            var hosts = config.Hosts.Where(host => host.Id.Equals(server, StringComparison.OrdinalIgnoreCase) ||
                host.Alias.Equals(server, StringComparison.OrdinalIgnoreCase)).ToList();
            return hosts.Count == 1 ? hosts : throw new ArgumentException("Server not found or ambiguous; use its ID.");
        }
        if (options.Get("group") is { } groupName)
        {
            var groups = config.Groups.Where(group => group.Id.Equals(groupName, StringComparison.OrdinalIgnoreCase) ||
                group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (groups.Count != 1) throw new ArgumentException("Group not found or ambiguous; use its ID.");
            return config.Hosts.Where(host => host.GroupId == groups[0].Id).ToList();
        }
        return new ServerTagService().Filter(config.Hosts, [options.Get("tag")!]);
    }

    public IReadOnlyList<ServerHost> RetryTargets(string reportPath, string ids)
    {
        var report = BatchReportFormatter.Read(reportPath);
        var selected = ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var failed = report.Results.Where(result => result.Status == BatchTargetStatus.Failed).Select(result => result.HostId).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0 || !selected.IsSubsetOf(failed)) throw new ArgumentException("Explicitly select failed report targets using --failed ID,ID.");
        var hosts = repository.Load().Hosts.Where(host => selected.Contains(host.Id)).ToList();
        if (hosts.Count != selected.Count) throw new ArgumentException("A failed target no longer exists. No retry was started.");
        return hosts;
    }
}
