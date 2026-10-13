using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ITableRendererService
{
    // A table view can change while the keyboard is idle (background DNS or cache expiry).
    long ViewRevision => 0;
    bool RefreshDue => false;
    void ResetViewRefresh() { }
    void RenderServerTable(AppConfig config);
    void RenderGroup(string groupName, List<ServerHost> hosts);
    string GetAuthLabel(AuthType authType);
}
