using Sshocked.Interfaces;

namespace Sshocked.Tests;

internal sealed class MacroEditorTestInput : IMacroEditorInputService
{
    public Queue<string?> Values { get; } = new();
    public List<(string Label, string? InitialValue)> Prompts { get; } = [];

    public string? Prompt(string label, string? initialValue = null)
    {
        Prompts.Add((label, initialValue));
        if (Values.Count == 0) throw new InvalidOperationException("Unexpected macro text prompt: " + label);
        return Values.Dequeue();
    }
}
