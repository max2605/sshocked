using Spectre.Console;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class ConfigBackupMenuTests
{
    [Fact]
    public async Task ExportAndInspectUseTheVersionedDocumentThroughTheMenu()
    {
        using var fixture = new ConfigBackupTestFixture();
        var paths = new DiagnosticTestPathInput();
        var file = fixture.PathFor("menu-backup.json");
        paths.Values.Enqueue(file);
        paths.Values.Enqueue(file);
        var menus = new MenuWorkflowScript();
        menus.Choose("Export portable backup");
        menus.Choose("Inspect backup");
        menus.Choose("Back");
        await new ConfigBackupMenuService(fixture.Backups, paths, menus, fixture.Audit, new MenuWorkflowConsole()).Show();
        Assert.Equal(1, fixture.Backups.Inspect(file).SchemaVersion);
        Assert.Contains(fixture.Audit.Output, output => output.Contains("Backup schema 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelAtRestoreConfirmationDoesNotWriteRecoveryOrModifyConfiguration()
    {
        using var fixture = new ConfigBackupTestFixture();
        fixture.Repository.Save(new() { Hosts = [ConfigBackupTestFixture.Host("current", "current")] });
        var input = fixture.Input(new() { Hosts = [ConfigBackupTestFixture.Host("incoming", "incoming")] });
        var paths = new DiagnosticTestPathInput();
        paths.Values.Enqueue(input);
        var menus = new MenuWorkflowScript();
        menus.Choose("Preview and restore");
        menus.Choose("Back");
        var previous = AnsiConsole.Console;
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        { Out = new AnsiConsoleOutput(TextWriter.Null), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.Yes });
        console.Profile.Capabilities.Interactive = true;
        AnsiConsole.Console = new ContainerMutationTestsPromptConsole(console, new ContainerMutationTestsPromptInput("\r\r", () => { }));
        try { await new ConfigBackupMenuService(fixture.Backups, paths, menus, fixture.Audit, new MenuWorkflowConsole()).Show(); }
        finally { AnsiConsole.Console = previous; }
        Assert.Equal("current", Assert.Single(fixture.Repository.Load().Hosts).Id);
        Assert.False(Directory.Exists(fixture.RecoveryDirectory));
    }
}
