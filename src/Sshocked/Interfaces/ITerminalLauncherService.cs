using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ITerminalLauncherService
{
    Task Launch(ServerHost host, ContainerModel? container = null);
}
