using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MenuShortcutAllocator
{
    private const string AvailableKeys = "1234567890abcdefghijklmnopqrstuvwxyz";

    public List<MenuEntry> Assign(IReadOnlyList<MenuEntry> entries)
    {
        var reserved = entries.Where(entry => entry.Shortcut.HasValue)
            .Select(entry => char.ToLowerInvariant(entry.Shortcut!.Value)).ToHashSet();
        var used = new HashSet<char>();
        return entries.Select(entry =>
        {
            var shortcut = entry.Shortcut;
            if (shortcut is not { } preferred || !used.Add(preferred))
            {
                // Reserve explicit action keys before assigning shortcuts to dynamic server rows.
                shortcut = AvailableKeys.Where(key => !reserved.Contains(key) && !used.Contains(key))
                    .Select(key => (char?)key).FirstOrDefault();
                if (shortcut is { } assigned) used.Add(assigned);
            }
            return new MenuEntry(entry.Label, shortcut, entry.Tag, entry.ExecuteAsync, entry.ActionType);
        }).ToList();
    }
}
