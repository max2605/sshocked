using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IMacroService
{
    IReadOnlyList<MacroDefinition> List();
    MacroDefinition Find(string nameOrId);
    MacroDefinition Save(MacroRecording recording, string name);
    void Delete(string id);
    Task<IReadOnlyList<MacroTargetResult>> Run(MacroDefinition macro, IReadOnlyList<ServerHost> targets,
        Action<string> progress, CancellationToken cancellationToken = default);
}
