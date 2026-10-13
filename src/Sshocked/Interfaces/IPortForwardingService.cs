using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IPortForwardingService : IAsyncDisposable
{
    IReadOnlyList<ForwardingProfile> List();
    ForwardingProfile Find(string nameOrId);
    void Save(ForwardingProfile profile, ForwardingProfile? expected = null);
    void Delete(string nameOrId);
    IReadOnlyList<ForwardingStatus> Status();
    Task<ForwardingStatus> Start(string nameOrId, CancellationToken cancellationToken = default);
    Task Stop(string nameOrId, CancellationToken cancellationToken = default);
    Task Wait(string nameOrId, CancellationToken cancellationToken = default);
}
