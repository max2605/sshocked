using Spectre.Console;

namespace Sshocked.Tests;

internal sealed class KeyboardShortcutRenderingInput : IAnsiConsoleInput
{
    private readonly Queue<(ConsoleKeyInfo? Key, Action? Resize)> steps = new();

    public void AddKey(ConsoleKey key, char character = '\0', ConsoleModifiers modifiers = 0) =>
        steps.Enqueue((new ConsoleKeyInfo(character, key, modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control)), null));
    public void AddResize(Action resize) => steps.Enqueue((null, resize));

    public bool IsKeyAvailable()
    {
        if (!steps.TryPeek(out var step)) throw new InvalidOperationException("The menu requested unexpected additional input.");
        if (step.Resize is null) return true;
        steps.Dequeue();
        step.Resize();
        return false;
    }

    public ConsoleKeyInfo? ReadKey(bool intercept)
    {
        var step = steps.Dequeue();
        if (step.Resize is not null) throw new InvalidOperationException("Resize events must be handled without reading a key.");
        return step.Key;
    }

    public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
}
