using System.Net;

namespace Sshocked.Interfaces;

public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string hostName, CancellationToken cancellationToken);
}
