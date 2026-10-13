using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IMacroMenuService
{
    Task Show();
    Task Run(IReadOnlyList<ServerHost> hosts);
}
