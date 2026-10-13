using Spectre.Console;
using Sshocked.Services;

namespace Sshocked.Tests;

internal sealed class MacroEditorTestFixture : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-macro-editor-" + Guid.NewGuid().ToString("N"));
    private readonly IAnsiConsole previousConsole = AnsiConsole.Console;
    public MacroTestDependencies Dependencies { get; } = new();
    public MacroEditorTestInput Input { get; } = new();
    public MenuWorkflowScript Menus { get; } = new();
    public MenuWorkflowConsole Helper { get; } = new();
    public JsonConfigRepository Repository { get; }
    public MacroService Macros { get; }
    public MacroEditorService Editor { get; }

    public MacroEditorTestFixture()
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        { Out = new AnsiConsoleOutput(TextWriter.Null), Interactive = InteractionSupport.No });
        Repository = new JsonConfigRepository(Path.Combine(directory, "appconfig.json"));
        Macros = new MacroService(Repository, Dependencies, Dependencies);
        Editor = new MacroEditorService(Macros, Menus, Dependencies, Helper, Input);
    }

    public void Dispose()
    {
        AnsiConsole.Console = previousConsole;
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
