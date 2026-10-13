using Sshocked.Interfaces;

namespace Sshocked.Tests;

internal sealed class DiagnosticTestPathInput : IPathInputService
{
    public Queue<string?> Values { get; } = new();
    public string? Prompt(string label, string? defaultValue = null, bool allowEmpty = true, Func<string, string?>? validate = null)
    {
        if (Values.Count == 0) throw new InvalidOperationException("Unexpected path prompt: " + label);
        return Values.Dequeue();
    }
}
