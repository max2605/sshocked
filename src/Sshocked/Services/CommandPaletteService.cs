using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class CommandPaletteService(CommandPaletteCatalog catalog) : ICommandPaletteService
{
    public MenuEntry? Show(IReadOnlyList<MenuEntry> current)
    {
        var console = AnsiConsole.Console;
        var candidates = current.Concat(catalog.Global()).DistinctBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var query = "";
        var selected = 0;
        while (true)
        {
            var matches = Search(candidates, query);
            selected = Math.Clamp(selected, 0, Math.Max(0, matches.Count - 1));
            if (console.Profile.Capabilities.Ansi) console.Clear();
            console.MarkupLine("[bold]Command palette[/]");
            console.WriteLine("Search: " + MonitoringDisplay.Literal(query));
            console.WriteLine("Type to filter | Up/Down select | Enter open | Esc returns");
            if (matches.Count == 0) console.WriteLine("No matches.");
            var first = Math.Max(0, selected - 8);
            for (var index = first; index < Math.Min(matches.Count, first + 12); index++)
            {
                var text = Markup.Escape(Plain(matches[index]));
                console.MarkupLine(index == selected ? "[black on cyan bold]> " + text + "[/]" : "  " + text);
            }
            console.WriteLine($"{matches.Count} matching action(s)");
            var input = console.Input.ReadKey(true);
            if (input is not { } key || key.Key == ConsoleKey.Escape) return null;
            if (key.Key == ConsoleKey.Enter && matches.Count > 0) return matches[selected];
            if (key.Key == ConsoleKey.UpArrow) { selected = Math.Max(0, selected - 1); continue; }
            if (key.Key == ConsoleKey.DownArrow) { selected = Math.Min(Math.Max(0, matches.Count - 1), selected + 1); continue; }
            if (key.Key == ConsoleKey.Home) { selected = 0; continue; }
            if (key.Key == ConsoleKey.End) { selected = Math.Max(0, matches.Count - 1); continue; }
            if (key.Key == ConsoleKey.Backspace) { if (query.Length > 0) query = query[..^1]; selected = 0; continue; }
            if (!char.IsControl(key.KeyChar) && query.Length < 160) { query += key.KeyChar; selected = 0; }
        }
    }

    public void ShowHelp(IReadOnlyList<MenuEntry> current)
    {
        var console = AnsiConsole.Console;
        if (console.Profile.Capabilities.Ansi) console.Clear();
        console.MarkupLine("[bold]Keyboard help for the current view[/]");
        console.WriteLine("Up/Down navigate; PageUp/PageDown move ten rows; Home/End jump.");
        console.WriteLine("Enter/Space selects; Esc returns; Ctrl+P searches; F1 opens this help.");
        foreach (var entry in current)
            console.WriteLine($"{(entry.Shortcut is { } key ? char.ToUpperInvariant(key).ToString() : "Arrows")}: {Plain(entry)}");
        console.WriteLine("Press a key to return to the previous selection.");
        console.Input.ReadKey(true);
    }

    public static IReadOnlyList<MenuEntry> Search(IEnumerable<MenuEntry> entries, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return entries.Where(entry => terms.All(term => Plain(entry).Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private static string Plain(MenuEntry entry) => MonitoringDisplay.Literal(Markup.Remove(entry.Label));
}
