using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class KeyboardShortcutService(IConfigRepository repository) : IKeyboardShortcutService
{
    public MenuEntry? ShowMenu(string title, List<MenuEntry> options)
    {
        if (options.Count == 0)
        {
            return null;
        }
        options = new MenuShortcutAllocator().Assign(options);
        var palette = UiThemePalette.For(repository.Load().UI.Theme);

        var shortcutMap = new Dictionary<char, MenuEntry>();
        foreach (var entry in options)
        {
            if (entry.Shortcut.HasValue && !shortcutMap.ContainsKey(entry.Shortcut.Value))
            {
                shortcutMap[entry.Shortcut.Value] = entry;
            }
        }

        var selectedIndex = 0;
        var firstRender = true;
        int renderedLines = 0;

        while (true)
        {
            if (!firstRender && renderedLines > 0)
            {
                Console.Write($"\x1b[{renderedLines}A\x1b[0J");
            }

            renderedLines = RenderMenu(title, options, selectedIndex, palette);
            firstRender = false;

            var key = System.Console.ReadKey(true);

            if (key.Key is ConsoleKey.UpArrow)
            {
                selectedIndex = selectedIndex > 0 ? selectedIndex - 1 : options.Count - 1;
                continue;
            }

            if (key.Key is ConsoleKey.DownArrow)
            {
                selectedIndex = (selectedIndex + 1) % options.Count;
                continue;
            }

            if (key.Key == ConsoleKey.PageUp)
            {
                selectedIndex = Math.Max(0, selectedIndex - 10);
                continue;
            }

            if (key.Key == ConsoleKey.PageDown)
            {
                selectedIndex = Math.Min(options.Count - 1, selectedIndex + 10);
                continue;
            }

            if (key.Key == ConsoleKey.Home)
            {
                selectedIndex = 0;
                continue;
            }

            if (key.Key == ConsoleKey.End)
            {
                selectedIndex = options.Count - 1;
                continue;
            }

            if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar)
            {
                System.Console.WriteLine();
                return options[selectedIndex];
            }

            if (key.Key == ConsoleKey.Escape)
            {
                System.Console.WriteLine();
                return FindBackOrExit(options);
            }

            if (key.KeyChar != '\0' && shortcutMap.TryGetValue(char.ToLowerInvariant(key.KeyChar), out var selected))
            {
                System.Console.WriteLine();
                return selected;
            }
        }
    }

    private static MenuEntry? FindBackOrExit(List<MenuEntry> options)
    {
        foreach (var entry in options)
        {
            if (entry.ActionType is MenuActionType.Back or MenuActionType.Exit)
            {
                return entry;
            }
        }
        return null;
    }

    private static int RenderMenu(string title, List<MenuEntry> options, int selectedIndex, UiThemePalette palette)
    {
        var lines = 0;

        AnsiConsole.MarkupLine(palette.AdaptMarkup(title));
        lines++;
        AnsiConsole.WriteLine();
        lines++;

        for (var i = 0; i < options.Count; i++)
        {
            var entry = options[i];
            var prefix = i == selectedIndex ? "> " : "  ";

            if (i == selectedIndex)
            {
                AnsiConsole.MarkupLine($"{prefix}[{palette.Selection}]{Markup.Escape(Markup.Remove(entry.DisplayLabel))}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"{prefix}{palette.AdaptMarkup(entry.DisplayLabel)}");
            }
            lines++;
        }

        var hint = $"[{palette.Muted}]↑↓ Navigate | Shortcut key | Enter Confirm | Esc = Back[/]";
        AnsiConsole.MarkupLine(hint);
        lines++;

        return lines;
    }
}
