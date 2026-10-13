namespace Sshocked.Interfaces;

public interface IMacroEditorInputService
{
    string? Prompt(string label, string? initialValue = null);
}
