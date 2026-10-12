using Spectre.Console;





namespace Sshocked.Tests;

internal sealed class ContainerMutationTestsPromptInput(string text, Action beforeFirstKey) : IAnsiConsoleInput
{
    private readonly Queue<char> _keys = new(text);
    private bool _first = true;
    public bool IsKeyAvailable() => _keys.Count > 0;
    public ConsoleKeyInfo? ReadKey(bool intercept)
    {
        if (_first)
        {
            _first = false;
            beforeFirstKey();
        }

        if (_keys.Count == 0)
            throw new InvalidOperationException("The test prompt requested unexpected additional input.");
        var character = _keys.Dequeue();
        return new ConsoleKeyInfo(character, character == '\r' ? ConsoleKey.Enter : ConsoleKey.A, false, false, false);
    }

    // Spectre.Console requires this interface member name.
    public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
}
