using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IBatchMenuService
{
    Task Show();
    Task RunCommand(Func<IReadOnlyList<ServerHost>> targets);
    Task RunMacro(Func<IReadOnlyList<ServerHost>> targets);
    Task RunMacro(Func<IReadOnlyList<ServerHost>> targets, MacroDefinition macro);
    Task Upload(Func<IReadOnlyList<ServerHost>> targets);
    Task Download(Func<IReadOnlyList<ServerHost>> targets);
}
