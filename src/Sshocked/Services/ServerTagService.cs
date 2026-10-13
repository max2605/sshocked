using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ServerTagService : IServerTagService
{
    public List<string> Normalize(IEnumerable<string> tags) => tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase).ToList();

    public List<string> GetAll(IEnumerable<ServerHost> hosts) => Normalize(hosts.SelectMany(host => host.Tags));

    public List<string> GetAllTargets(IEnumerable<ServerHost> hosts) => Normalize(hosts.SelectMany(host => host.Tags.Concat(host.SavedContainers.SelectMany(container => container.Tags))));

    public List<ConnectionTarget> FilterTargets(IEnumerable<ServerHost> hosts, IReadOnlyCollection<string> tags)
    {
        var required = Normalize(tags);
        bool Match(List<string> values) => required.All(tag => values.Any(value => string.Equals(value?.Trim(), tag, StringComparison.OrdinalIgnoreCase)));
        return hosts.SelectMany(host => (Match(host.Tags) ? new[] { new ConnectionTarget(host, null) } : [])
                .Concat(host.SavedContainers.Where(container => Match(container.Tags)).Select(container => new ConnectionTarget(host, container))))
            .OrderBy(target => target.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<ServerHost> Filter(IEnumerable<ServerHost> hosts, IReadOnlyCollection<string> tags)
    {
        var required = Normalize(tags);
        // Tags complement the single static group: matching every selected tag creates a live intersection.
        return hosts.Where(host => required.All(tag => host.Tags.Any(value =>
                !string.IsNullOrWhiteSpace(value) && string.Equals(value.Trim(), tag, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(host => host.IsFavorite).ThenBy(host => host.Alias, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
