using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class CommandPaletteTests
{
    [Fact]
    public void SearchIsCaseInsensitiveAndReturnsTheOriginalSharedActions()
    {
        var host = new MenuEntry("Server: prod[[blue]] host.example");
        var group = new MenuEntry("Group: Production");
        var result = CommandPaletteService.Search([host, group], "SERVER PROD");
        Assert.Same(host, Assert.Single(result));
        Assert.Empty(CommandPaletteService.Search([host, group], "missing"));
    }

    [Fact]
    public void PaletteSearchFindsTargetsBeyondTheShortcutAlphabet()
    {
        using var fixture = new KeyboardShortcutRenderingFixture();
        var targets = Enumerable.Range(0, 100).Select(index => new MenuEntry($"Server: host-{index}")).ToList();
        var catalog = new CommandPaletteCatalog();
        catalog.Register(() => targets);
        fixture.Input.AddKey(ConsoleKey.H, 'h');
        fixture.Input.AddKey(ConsoleKey.O, 'o');
        fixture.Input.AddKey(ConsoleKey.S, 's');
        fixture.Input.AddKey(ConsoleKey.T, 't');
        fixture.Input.AddKey(ConsoleKey.OemMinus, '-');
        fixture.Input.AddKey(ConsoleKey.D9, '9');
        fixture.Input.AddKey(ConsoleKey.D9, '9');
        fixture.Input.AddKey(ConsoleKey.Enter);
        var selected = new CommandPaletteService(catalog).Show([]);
        Assert.Same(targets[99], selected);
    }

    [Fact]
    public void EscapeFromPaletteReturnsWithoutExecutingOrChangingSharedActions()
    {
        using var fixture = new KeyboardShortcutRenderingFixture();
        var calls = 0;
        var entry = new MenuEntry("Host [[green]]", executeAsync: () => { calls++; return Task.CompletedTask; });
        fixture.Input.AddKey(ConsoleKey.Escape);
        Assert.Null(new CommandPaletteService(new()).Show([entry]));
        Assert.Equal(0, calls);
        Assert.Contains("[green]", fixture.Output.ToString());
    }

    [Fact]
    public void HelpUsesTheActuallyAssignedShortcut()
    {
        using var fixture = new KeyboardShortcutRenderingFixture();
        fixture.Input.AddKey(ConsoleKey.Escape);
        new CommandPaletteService(new()).ShowHelp([new("Connect", 'c'), new("Many targets")]);
        var output = fixture.Output.ToString();
        Assert.Contains("C: Connect", output);
        Assert.Contains("Arrows: Many targets", output);
    }

    [Fact]
    public void ControlPEscapeRestoresTheCallersSelectedMenuEntry()
    {
        using var fixture = new KeyboardShortcutRenderingFixture();
        fixture.Input.AddKey(ConsoleKey.DownArrow);
        fixture.Input.AddKey(ConsoleKey.P, '\u0010', ConsoleModifiers.Control);
        fixture.Input.AddKey(ConsoleKey.Escape);
        fixture.Input.AddKey(ConsoleKey.Enter);
        var selected = new MenuEntry("Second action", 's');
        var service = new KeyboardShortcutService(fixture.Repository, commandPalette: new CommandPaletteService(new()));
        var result = service.ShowMenu("Menu", [new("First action", 'f'), selected]);
        Assert.Equal(selected.Label, result!.Label);
        Assert.Equal(selected.Shortcut, result.Shortcut);
    }
}
