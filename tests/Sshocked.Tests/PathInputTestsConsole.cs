using Sshocked.Interfaces;

namespace Sshocked.Tests;

internal sealed class PathInputTestsConsole(IEnumerable<string?> values) : IConsoleWriterService
{
    private readonly Queue<string?> _values = new(values);
    public List<string> Output { get; } = [];
    public void WriteLine(string message) => Output.Add(message);
    public void Write(string message) => Output.Add(message);
    public string? ReadLine() => _values.Count > 0 ? _values.Dequeue() : null;
}
