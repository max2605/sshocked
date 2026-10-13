using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ITargetViewService
{
    IReadOnlyList<TargetViewDefinition> List();
    TargetViewDefinition Find(string nameOrId);
    TargetViewDefinition Save(string name, string expression, string? id = null);
    void Delete(string nameOrId);
    IReadOnlyList<ServerHost> Resolve(string nameOrId);
}
