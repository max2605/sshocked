using System.Net;
using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class DnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string hostName, CancellationToken cancellationToken)
        => Dns.GetHostAddressesAsync(hostName, cancellationToken);
}
