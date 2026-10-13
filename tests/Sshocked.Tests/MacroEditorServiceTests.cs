using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class MacroEditorServiceTests
{
    [Fact]
    public async Task CreateEditDeleteAndReorderSaveTheDisplayedSequenceWithoutSsh()
    {
        using var fixture = new MacroEditorTestFixture();
        fixture.Input.Values.Enqueue("deployment");
        fixture.Input.Values.Enqueue("cd /srv/app");
        fixture.Input.Values.Enqueue("printf 'ready'");
        fixture.Input.Values.Enqueue("  printf '%s' 'ready'  ");
        fixture.Input.Values.Enqueue("printf 'remove'");
        fixture.Menus.Choose(MacroEditorLabels.Name);
        fixture.Menus.Choose(MacroEditorLabels.Add);
        fixture.Menus.Choose(MacroEditorLabels.Add);
        fixture.Menus.Choose(MacroEditorLabels.EditCommand);
        fixture.Menus.Choose("2.");
        fixture.Menus.Choose(MacroEditorLabels.MoveUp);
        fixture.Menus.Choose("2.");
        fixture.Menus.Choose(MacroEditorLabels.Add);
        fixture.Menus.Choose(MacroEditorLabels.DeleteCommand);
        fixture.Menus.Choose("3.");
        fixture.Menus.Choose(MacroEditorLabels.View);
        fixture.Menus.Choose(MacroEditorLabels.Save);

        var saved = await fixture.Editor.Create();

        Assert.NotNull(saved);
        Assert.Equal(["  printf '%s' 'ready'  ", "cd /srv/app"], saved.Commands);
        Assert.Equal(saved.Commands, fixture.Macros.Find(saved.Id).Commands);
        Assert.Null(saved.RecordedOnHostId);
        Assert.Equal("printf 'ready'", fixture.Input.Prompts[3].InitialValue);
        Assert.Contains("1.   printf '%s' 'ready'  ", fixture.Dependencies.Output);
        Assert.Empty(fixture.Dependencies.Commands);
        Assert.Empty(fixture.Dependencies.Targets);
    }

    [Fact]
    public async Task BackFromUnsavedDialogKeepsDraftAndDiscardLeavesOriginalUnchanged()
    {
        using var fixture = new MacroEditorTestFixture();
        var original = fixture.Macros.Create("health", ["uptime"]);
        fixture.Input.Values.Enqueue("df -h");
        fixture.Menus.Choose(MacroEditorLabels.Add);
        fixture.Menus.Choose(MacroEditorLabels.Back);
        fixture.Menus.Choose(MacroEditorLabels.Back);
        fixture.Menus.Choose(MacroEditorLabels.View);
        fixture.Menus.Choose(MacroEditorLabels.Back);
        fixture.Menus.Choose(MacroEditorLabels.Discard);

        Assert.Null(await fixture.Editor.Edit(original));
        Assert.Equal(["uptime"], fixture.Macros.Find(original.Id).Commands);
        Assert.Contains("2. df -h", fixture.Dependencies.Output);
        Assert.DoesNotContain(fixture.Dependencies.Events, entry => entry.EventType == MacroEventNames.Updated);
    }

    [Fact]
    public async Task SaveFromUnsavedDialogPersistsEditAndPreservesIdentity()
    {
        using var fixture = new MacroEditorTestFixture();
        var original = fixture.Macros.Create("health", ["uptime"]);
        fixture.Input.Values.Enqueue("df -h");
        fixture.Menus.Choose(MacroEditorLabels.Add);
        fixture.Menus.Choose(MacroEditorLabels.Back);
        fixture.Menus.Choose(MacroEditorLabels.Save);

        var saved = await fixture.Editor.Edit(original);
        Assert.NotNull(saved);
        Assert.Equal(original.Id, saved.Id);
        Assert.Equal(original.CreatedAt, saved.CreatedAt);
        Assert.Equal(["uptime", "df -h"], saved.Commands);
    }

    [Fact]
    public async Task InvalidSaveKeepsDraftAvailableForCorrection()
    {
        using var fixture = new MacroEditorTestFixture();
        fixture.Input.Values.Enqueue("health");
        fixture.Input.Values.Enqueue("   ");
        fixture.Input.Values.Enqueue("uptime");
        fixture.Menus.Choose(MacroEditorLabels.Save, () => Assert.Empty(fixture.Macros.List()));
        fixture.Menus.Choose(MacroEditorLabels.Name);
        fixture.Menus.Choose(MacroEditorLabels.Save, () => Assert.Empty(fixture.Macros.List()));
        fixture.Menus.Choose(MacroEditorLabels.Add);
        fixture.Menus.Choose(MacroEditorLabels.Add);
        fixture.Menus.Choose(MacroEditorLabels.Save);

        var saved = await fixture.Editor.Create();
        Assert.NotNull(saved);
        Assert.Equal(["uptime"], saved.Commands);
        Assert.Equal(3, fixture.Helper.Waits);
    }

    [Fact]
    public async Task DuplicateRequiresANewNameAndDoesNotChangeTheOriginal()
    {
        using var fixture = new MacroEditorTestFixture();
        var original = fixture.Macros.Create("health", ["uptime"]);
        fixture.Input.Values.Enqueue("health copy");
        fixture.Menus.Choose(MacroEditorLabels.Save, () => Assert.Single(fixture.Macros.List()));
        fixture.Menus.Choose(MacroEditorLabels.Name);
        fixture.Menus.Choose(MacroEditorLabels.Save);

        var saved = await fixture.Editor.Duplicate(original);
        Assert.NotNull(saved);
        Assert.NotEqual(original.Id, saved.Id);
        Assert.Equal(string.Empty, fixture.Input.Prompts.Single().InitialValue);
        Assert.Equal("health", fixture.Macros.Find(original.Id).Name);
        Assert.Equal(2, fixture.Macros.List().Count);
        Assert.Empty(fixture.Dependencies.Commands);
    }

    [Fact]
    public async Task TemplateBecomesAnEditableDraftWithoutExecutingPackageCommands()
    {
        using var fixture = new MacroEditorTestFixture();
        fixture.Input.Values.Enqueue("packages");
        fixture.Input.Values.Enqueue("sudo -n apt upgrade");
        fixture.Menus.Choose("Ubuntu/Debian");
        fixture.Menus.Choose(MacroEditorLabels.Name);
        fixture.Menus.Choose(MacroEditorLabels.EditCommand);
        fixture.Menus.Choose("2.");
        fixture.Menus.Choose(MacroEditorLabels.Save);

        var saved = await fixture.Editor.CreateFromTemplate();
        Assert.NotNull(saved);
        Assert.Equal(["sudo -n apt update", "sudo -n apt upgrade"], saved.Commands);
        Assert.Equal("sudo -n apt upgrade -y", MacroTemplates.All[0].Commands[1]);
        Assert.Empty(fixture.Dependencies.Commands);
    }

    [Fact]
    public async Task ConcurrentDeletionReportsConflictAndRetainsUnsavedDraftUntilDiscard()
    {
        using var fixture = new MacroEditorTestFixture();
        var original = fixture.Macros.Create("health", ["uptime"]);
        fixture.Input.Values.Enqueue("df -h");
        fixture.Menus.Choose(MacroEditorLabels.Add, () => fixture.Macros.Delete(original.Id));
        fixture.Menus.Choose(MacroEditorLabels.Save);
        fixture.Menus.Choose(MacroEditorLabels.View);
        fixture.Menus.Choose(MacroEditorLabels.Back);
        fixture.Menus.Choose(MacroEditorLabels.Discard);

        Assert.Null(await fixture.Editor.Edit(original));
        Assert.Empty(fixture.Macros.List());
        Assert.Contains(fixture.Dependencies.Output, output => output.Contains("changed or deleted elsewhere", StringComparison.Ordinal));
        Assert.Contains("2. df -h", fixture.Dependencies.Output);
    }

    [Fact]
    public async Task UnchangedAndEmptyDraftsReturnWithoutUnsavedDialog()
    {
        using var fixture = new MacroEditorTestFixture();
        fixture.Menus.Choose(MacroEditorLabels.Back);
        Assert.Null(await fixture.Editor.Create());
        var original = fixture.Macros.Create("health", ["uptime"]);
        fixture.Menus.Choose(MacroEditorLabels.Back);
        Assert.Null(await fixture.Editor.Edit(original));
        Assert.Equal(2, fixture.Menus.Menus.Count);
    }

    [Fact]
    public void InputPreservesExactTextAndKeepsExistingMultilineValue()
    {
        using var fixture = new MacroEditorTestFixture();
        var input = new MacroEditorInputService(fixture.Dependencies);
        fixture.Dependencies.Input.Enqueue("  printf '%s' \"ready\"  ");
        fixture.Dependencies.Input.Enqueue("");
        fixture.Dependencies.Input.Enqueue(null);
        Assert.Equal("  printf '%s' \"ready\"  ", input.Prompt("Command"));
        Assert.Equal("printf first\nprintf second", input.Prompt("Command", "printf first\nprintf second"));
        Assert.Null(input.Prompt("Command", "uptime"));
    }
}
