using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IMacroEditorService
{
    Task<MacroDefinition?> Create();
    Task<MacroDefinition?> CreateFromTemplate();
    Task<MacroDefinition?> Edit(MacroDefinition original);
    Task<MacroDefinition?> Duplicate(MacroDefinition original);
}
