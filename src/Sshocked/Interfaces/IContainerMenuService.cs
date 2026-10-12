using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IContainerMenuService
{
    Task ShowScannedActions(ServerHost host, ContainerModel container);
    Task ShowSavedActions(ServerHost host, ContainerModel container, bool allowManagement = true);
    Task SelectFromGroup(AppConfig config, List<ContainerModel> containers);
    void RenderGroup(string groupName, List<ContainerModel> containers, AppConfig config);
}
