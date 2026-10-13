using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class KeyboardShortcutRenderingTable : ITableRendererService
{
    public long ViewRevision { get; set; }
    public bool RefreshDue { get; set; }
    public int ResetCalls { get; private set; }
    public void ResetViewRefresh() { ResetCalls++; RefreshDue = false; }
    public void RenderServerTable(AppConfig config) { }
    public void RenderGroup(string groupName, List<ServerHost> hosts) { }
    public string GetAuthLabel(AuthType authType) => "";
}
