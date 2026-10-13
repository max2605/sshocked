using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

internal sealed class KeyboardShortcutRenderingFixture : IDisposable
{
    private readonly IAnsiConsole previous = AnsiConsole.Console;
    public StringWriter Output { get; } = new();
    public KeyboardShortcutRenderingInput Input { get; } = new();
    public IAnsiConsole Console { get; }
    public TableRendererTestsMemoryRepository Repository { get; } = new(new AppConfig());
    public KeyboardShortcutService Keyboard { get; }

    public KeyboardShortcutRenderingFixture(int width = 80, int height = 100,
        ITableRendererService? tableRenderer = null, TimeProvider? timeProvider = null)
    {
        var inner = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Output),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes
        });
        inner.Profile.Width = width;
        inner.Profile.Height = height;
        inner.Profile.Capabilities.Interactive = true;
        Console = new ContainerMutationTestsPromptConsole(inner, Input);
        AnsiConsole.Console = Console;
        Keyboard = new KeyboardShortcutService(Repository, tableRenderer, timeProvider);
    }

    public void Dispose()
    {
        AnsiConsole.Console = previous;
        Output.Dispose();
    }
}
