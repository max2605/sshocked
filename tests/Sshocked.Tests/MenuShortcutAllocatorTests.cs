using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MenuShortcutAllocatorTests
{
    [Fact]
    public void RecentRowsReceiveUniqueKeysAndPreserveFixedActions()
    {
        var targets = Enumerable.Range(1, 15).Select(index => new MenuEntry("Same alias", tag: index)).ToList();
        targets.Add(new("Clear recents", 'L'));
        targets.Add(new("Back", 'B', actionType: MenuActionType.Back));
        var entries = new MenuShortcutAllocator().Assign(targets);
        Assert.All(entries, entry => Assert.NotNull(entry.Shortcut));
        Assert.Equal(entries.Count, entries.Select(entry => entry.Shortcut).Distinct().Count());
        Assert.Equal('l', entries[^2].Shortcut);
        Assert.Equal('b', entries[^1].Shortcut);
        Assert.Equal(Enumerable.Range(1, 15), entries.Take(15).Select(entry => (int)entry.Tag!));
    }

    [Fact]
    public void DuplicateExplicitKeysAreReallocatedWithoutChangingActions()
    {
        Func<Task> callback = () => Task.CompletedTask;
        var source = new List<MenuEntry> { new("One", 'A', executeAsync: callback), new("Two", 'a'), new("Three", 'J'), new("Back", 'B', actionType: MenuActionType.Back) };
        var allocator = new MenuShortcutAllocator();
        var entries = allocator.Assign(source);
        Assert.Equal('a', entries[0].Shortcut);
        Assert.NotEqual('a', entries[1].Shortcut);
        Assert.Equal('j', entries[2].Shortcut);
        Assert.Same(callback, entries[0].ExecuteAsync);
        Assert.Equal(MenuActionType.Back, entries[^1].ActionType);
        Assert.Equal(entries.Select(entry => entry.Shortcut), allocator.Assign(source).Select(entry => entry.Shortcut));
        Assert.Equal('a', source[1].Shortcut);
    }

    [Fact]
    public void ExcessRowsRemainSelectableWithoutRepeatingShortcutKeys()
    {
        var entries = new MenuShortcutAllocator().Assign(Enumerable.Range(0, 50).Select(index => new MenuEntry(index.ToString())).ToList());
        var shortcuts = entries.Where(entry => entry.Shortcut.HasValue).Select(entry => entry.Shortcut).ToList();
        Assert.Equal(36, shortcuts.Count);
        Assert.Equal(shortcuts.Count, shortcuts.Distinct().Count());
        Assert.Equal(14, entries.Count(entry => !entry.Shortcut.HasValue));
    }
}
