using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IServerTagService
{
    List<string> Normalize(IEnumerable<string> tags);
    List<string> GetAll(IEnumerable<ServerHost> hosts);
    List<ServerHost> Filter(IEnumerable<ServerHost> hosts, IReadOnlyCollection<string> tags);
    List<string> GetAllTargets(IEnumerable<ServerHost> hosts) => GetAll(hosts);
    List<ConnectionTarget> FilterTargets(IEnumerable<ServerHost> hosts, IReadOnlyCollection<string> tags)
        => Filter(hosts, tags).Select(host => new ConnectionTarget(host, null)).ToList();
}
