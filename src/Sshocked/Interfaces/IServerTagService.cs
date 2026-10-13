using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IServerTagService
{
    List<string> Normalize(IEnumerable<string> tags);
    List<string> GetAll(IEnumerable<ServerHost> hosts);
    List<ServerHost> Filter(IEnumerable<ServerHost> hosts, IReadOnlyCollection<string> tags);
}
