using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IManagedCommandSessionService
{
    Task Show(ServerHost host);
}
