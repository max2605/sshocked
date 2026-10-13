using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IContainerMenuService
{
    Task Manage() => throw new NotSupportedException();
    Task ShowBatchActions(IReadOnlyList<ConnectionTarget> targets) => throw new NotSupportedException();
    Task ShowScannedActions(ServerHost host, ContainerModel container);
    Task ShowSavedActions(ServerHost host, ContainerModel container, bool allowManagement = true);
    Task SelectFromGroup(AppConfig config, List<ContainerModel> containers);
    void RenderGroup(string groupName, List<ContainerModel> containers, AppConfig config);
}
