using Spectre.Console;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class DiagnosticMenuTests
{
    [Fact]
    public async Task MenuPreviewsApplicationReportAndAllowsCancellingTheLocalExport()
    {
        var dependencies = new DiagnosticTestDependencies();
        var service = new DiagnosticService(dependencies, dependencies, dependencies, dependencies, dependencies);
        var paths = new DiagnosticTestPathInput();
        paths.Values.Enqueue(null);
        var menus = new MenuWorkflowScript();
        menus.Choose("Create diagnostic report");
        menus.Choose("Back");
        var previous = AnsiConsole.Console;
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        { Out = new AnsiConsoleOutput(TextWriter.Null), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.Yes });
        console.Profile.Capabilities.Interactive = true;
        AnsiConsole.Console = new ContainerMutationTestsPromptConsole(console, new ContainerMutationTestsPromptInput("\r\r\r", () => { }));
        try { await new DiagnosticMenuService(service, dependencies, paths, menus, dependencies, new MenuWorkflowConsole()).Show(); }
        finally { AnsiConsole.Console = previous; }
        Assert.Contains(dependencies.Output, output => output.Contains("No upload", StringComparison.Ordinal));
        Assert.Equal(0, dependencies.ProbeCalls);
        Assert.Empty(paths.Values);
    }
}
