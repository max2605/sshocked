using Spectre.Console;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class MacroMenuWorkflowTests
{
    [Fact]
    public async Task ServerActionRefreshesHostIdsAndUsesSharedBatchPreview()
    {
        using var fixture = new MacroEditorTestFixture();
        var original = new ServerHost { Id = "selected", Alias = "web", HostName = "before.example" };
        fixture.Repository.Update(config => config.Hosts = [original]);
        var batches = new MacroBatchMenuTestDependencies
        {
            BeforePreview = () => fixture.Repository.Update(config => config.Hosts[0].HostName = "after.example")
        };
        var menu = new MacroMenuService(fixture.Macros, new MenuWorkflowTargets(), fixture.Repository,
            fixture.Menus, fixture.Dependencies, fixture.Helper, fixture.Editor, batches);

        await menu.Run([original, new() { Id = "deleted" }]);

        Assert.Equal("after.example", Assert.Single(batches.Targets).HostName);
        Assert.Null(batches.Macro);
        Assert.Empty(fixture.Dependencies.Commands);
    }

    [Fact]
    public async Task DetailActionUsesSelectedMacroAndFreshTargetsWithoutSecondMacroSelection()
    {
        using var fixture = new MacroEditorTestFixture();
        var saved = fixture.Macros.Create("health", ["uptime"]);
        fixture.Repository.Update(config => config.Hosts = [new() { Id = "selected", Alias = "web", HostName = "before.example" }]);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        { Out = new AnsiConsoleOutput(TextWriter.Null), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.Yes });
        console.Profile.Capabilities.Interactive = true;
        AnsiConsole.Console = new ContainerMutationTestsPromptConsole(console, new ContainerMutationTestsPromptInput("\r\r", () => { }));
        var batches = new MacroBatchMenuTestDependencies
        {
            BeforePreview = () => fixture.Repository.Update(config => config.Hosts[0].HostName = "after.example")
        };
        var menu = new MacroMenuService(fixture.Macros, new MenuWorkflowTargets(), fixture.Repository,
            fixture.Menus, fixture.Dependencies, fixture.Helper, fixture.Editor, batches);
        fixture.Menus.Choose("Browse saved macros");
        fixture.Menus.Choose("Run on server, group or tag");
        fixture.Menus.Choose(MacroEditorLabels.Back);
        fixture.Menus.Choose(MacroEditorLabels.Back);

        await menu.Show();

        Assert.Equal(saved.Id, batches.Macro?.Id);
        Assert.Equal("after.example", Assert.Single(batches.Targets).HostName);
        Assert.Empty(fixture.Dependencies.Commands);
    }
}
