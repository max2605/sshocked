using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IMacroService
{
    IReadOnlyList<MacroDefinition> List();
    MacroDefinition Find(string nameOrId);
    MacroDefinition Save(MacroRecording recording, string name);
    MacroDefinition Create(string name, IReadOnlyList<string> commands) =>
        throw new NotSupportedException("This macro service does not support manual creation.");
    MacroDefinition Update(MacroDefinition original, string name, IReadOnlyList<string> commands) =>
        throw new NotSupportedException("This macro service does not support editing.");
    MacroDefinition Duplicate(MacroDefinition original, string name, IReadOnlyList<string> commands) =>
        throw new NotSupportedException("This macro service does not support duplication.");
    void Delete(string id);
    Task<IReadOnlyList<MacroTargetResult>> Run(MacroDefinition macro, IReadOnlyList<ServerHost> targets,
        Action<string> progress, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MacroTargetResult>> RunNonInteractive(MacroDefinition macro, IReadOnlyList<ServerHost> targets,
        Action<string> progress, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This macro service does not support noninteractive batch execution.");
}
