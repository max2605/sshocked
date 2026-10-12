using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IGroupSessionService
{
    Task ConnectServers(AppConfig config, List<ServerHost> hosts);
    Task ConnectContainers(AppConfig config, List<ContainerModel> containers);
    Task RunServerCommands(List<ServerHost> hosts);
    Task RunContainerCommands(AppConfig config, List<ContainerModel> containers);
}
