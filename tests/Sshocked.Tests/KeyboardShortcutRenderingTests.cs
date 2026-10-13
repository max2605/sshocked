using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class KeyboardShortcutRenderingTests
{
    [Fact]
    public void WrappedMenuRedrawRewindsPhysicalRows()
    {
        using var fixture = new KeyboardShortcutRenderingFixture(width: 24);
        fixture.Input.AddKey(ConsoleKey.DownArrow);
        fixture.Input.AddKey(ConsoleKey.Enter);
        var options = new List<MenuEntry> { new("A label long enough to wrap across several terminal rows"), new("Second") };

        var selected = fixture.Keyboard.ShowMenu("A title that also wraps across several terminal rows", options);

        Assert.Equal("Second", selected?.Label);
        var output = fixture.Output.ToString();
        var rewind = Regex.Match(output, "\u001b\\[(\\d+)A");
        Assert.True(rewind.Success, output);
        var firstFrame = RemoveAnsi(output[..rewind.Index]);
        var physicalRows = firstFrame.Count(character => character == '\n');
        Assert.True(physicalRows > options.Count + 3);
        Assert.Equal(physicalRows, int.Parse(rewind.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(160, 100)]
    [InlineData(80, 45)]
    public void IdleResizeRepaintsWholeViewAndPreservesSelection(int width, int height)
    {
        using var fixture = new KeyboardShortcutRenderingFixture();
        var frames = new List<(int Width, int Height)>();
        fixture.Input.AddKey(ConsoleKey.DownArrow);
        fixture.Input.AddResize(() =>
        {
            fixture.Console.Profile.Width = width;
            fixture.Console.Profile.Height = height;
        });
        fixture.Input.AddKey(ConsoleKey.Enter);

        var selected = fixture.Keyboard.ShowMenu("Menu", [new("First"), new("Second")], () =>
        {
            fixture.Console.Clear();
            frames.Add((fixture.Console.Profile.Width, fixture.Console.Profile.Height));
            fixture.Console.MarkupLine("Header marker");
            fixture.Console.Write(new Table().AddColumn("Alias").AddRow("Server marker").Expand());
            fixture.Console.MarkupLine("Footer marker");
        });

        Assert.Equal("Second", selected?.Label);
        Assert.Equal([(80, 100), (80, 100), (width, height)], frames);
        var output = RemoveAnsi(fixture.Output.ToString());
        Assert.Equal(3, Regex.Matches(output, "Header marker").Count);
        Assert.Equal(3, Regex.Matches(output, "Server marker").Count);
        Assert.Equal(3, Regex.Matches(output, "Footer marker").Count);
        Assert.DoesNotMatch("\u001b\\[\\d+A", fixture.Output.ToString());
    }

    [Fact]
    public void IdleDnsCompletionAndCacheExpiryRepaintViewWithoutChangingSelection()
    {
        var changes = new KeyboardShortcutRenderingTable();
        var clock = new HostReachabilityTestsClock();
        using var fixture = new KeyboardShortcutRenderingFixture(tableRenderer: changes, timeProvider: clock);
        var frames = 0;
        fixture.Input.AddKey(ConsoleKey.DownArrow);
        fixture.Input.AddResize(() => { changes.ViewRevision++; clock.Now += TimeSpan.FromSeconds(1); });
        fixture.Input.AddResize(() => { changes.RefreshDue = true; clock.Now += TimeSpan.FromSeconds(1); });
        fixture.Input.AddKey(ConsoleKey.Enter);

        var selected = fixture.Keyboard.ShowMenu("Menu", [new("First"), new("Second")], () =>
        {
            fixture.Console.Clear();
            changes.RefreshDue = false;
            fixture.Console.WriteLine("DNS result marker");
            frames++;
        });

        Assert.Equal("Second", selected?.Label);
        Assert.Equal(4, frames); // Initial, selection change, completed lookup, cache expiry.
        Assert.Equal(4, Regex.Matches(RemoveAnsi(fixture.Output.ToString()), "DNS result marker").Count);
    }

    [Fact]
    public void ViewWithoutTableClearsThePreviousDnsRefreshScope()
    {
        var changes = new KeyboardShortcutRenderingTable { RefreshDue = true };
        var clock = new HostReachabilityTestsClock();
        using var fixture = new KeyboardShortcutRenderingFixture(tableRenderer: changes, timeProvider: clock);
        var frames = 0;
        fixture.Input.AddResize(() => clock.Now += TimeSpan.FromSeconds(1));
        fixture.Input.AddKey(ConsoleKey.Enter);

        fixture.Keyboard.ShowMenu("Tags", [new("Back")], () =>
        {
            Assert.False(changes.RefreshDue);
            fixture.Console.Clear();
            frames++;
        });

        Assert.Equal(1, frames);
        Assert.Equal(1, changes.ResetCalls);
    }

    [Fact]
    public void BackgroundChangesDoNotRepaintLegacyMenusWithoutAViewCallback()
    {
        var changes = new KeyboardShortcutRenderingTable();
        var clock = new HostReachabilityTestsClock();
        using var fixture = new KeyboardShortcutRenderingFixture(tableRenderer: changes, timeProvider: clock);
        fixture.Input.AddResize(() => { changes.ViewRevision++; changes.RefreshDue = true; clock.Now += TimeSpan.FromSeconds(1); });
        fixture.Input.AddKey(ConsoleKey.Enter);

        var selected = fixture.Keyboard.ShowMenu("Menu marker", [new("First")]);

        Assert.Equal("First", selected?.Label);
        Assert.Single(Regex.Matches(RemoveAnsi(fixture.Output.ToString()), "Menu marker"));
    }

    [Fact]
    public async Task MenuFactoryRepaintsFooterAndExecutesSelectionOnlyOnce()
    {
        using var fixture = new KeyboardShortcutRenderingFixture();
        var chrome = new KeyboardShortcutRenderingChrome();
        var factory = new MenuFactory(fixture.Keyboard, chrome, new MenuWorkflowConsole(), fixture.Repository, NullLogger<MenuFactory>.Instance);
        var frames = 0;
        var executions = 0;
        fixture.Input.AddKey(ConsoleKey.DownArrow);
        fixture.Input.AddResize(() => fixture.Console.Profile.Width = 160);
        fixture.Input.AddKey(ConsoleKey.Enter);

        var continuing = await factory.RunMenu("Menu", [new("First"), new("Second", executeAsync: () => { executions++; return Task.CompletedTask; })], () =>
        {
            fixture.Console.Clear();
            frames++;
        });

        Assert.True(continuing);
        Assert.Equal(3, frames);
        Assert.Equal(3, chrome.Footers);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task LegacyMenuFactoryDefaultOverloadRendersOnceAndDelegates()
    {
        var script = new MenuWorkflowScript();
        script.Choose("Back");
        IMenuFactory factory = script;
        var frames = 0;

        var continuing = await factory.RunMenu("Menu", [new("Back", actionType: MenuActionType.Back)], () => frames++);

        Assert.False(continuing);
        Assert.Equal(1, frames);
        Assert.Single(script.Menus);
    }

    [Fact]
    public void LegacyKeyboardDefaultOverloadRendersOnceAndDelegates()
    {
        var keyboard = new KeyboardShortcutRenderingLegacyKeyboard();
        IKeyboardShortcutService service = keyboard;
        var frames = 0;

        var selected = service.ShowMenu("Menu", [new("First")], () => frames++);

        Assert.Equal("First", selected?.Label);
        Assert.Equal(1, frames);
        Assert.Equal(1, keyboard.Calls);
    }

    private static string RemoveAnsi(string value) => Regex.Replace(value, "\u001b\\[[0-?]*[ -/]*[@-~]", "");
}
