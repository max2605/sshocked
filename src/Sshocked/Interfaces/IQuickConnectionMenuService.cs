using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IQuickConnectionMenuService
{
    Task ConnectServer(AppConfig config);
    Task ConnectGroup(AppConfig config);
    Task RunGroupCommand(AppConfig config);
}
