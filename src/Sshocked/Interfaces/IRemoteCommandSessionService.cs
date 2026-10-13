using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IRemoteCommandSessionService
{
    Task<IManagedCommandSession> Open(ServerHost host, CancellationToken cancellationToken = default);
}
