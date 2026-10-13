using Spectre.Console;
using Spectre.Console.Rendering;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class KeyboardShortcutService(IConfigRepository repository, ITableRendererService? tableRenderer = null,
    TimeProvider? timeProvider = null) : IKeyboardShortcutService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public MenuEntry? ShowMenu(string title, List<MenuEntry> options) => ShowMenuCore(title, options, null);

    public MenuEntry? ShowMenu(string title, List<MenuEntry> options, Action renderView) => ShowMenuCore(title, options, renderView);

    private MenuEntry? ShowMenuCore(string title, List<MenuEntry> options, Action? renderView)
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
        var console = AnsiConsole.Console;
        var renderedSize = (Width: console.Profile.Width, Height: console.Profile.Height);
        var renderedLines = 0;
        var redraw = true;
        var renderedRevision = tableRenderer?.ViewRevision ?? 0;
        var nextViewPoll = DateTimeOffset.MinValue;

        while (true)
        {
            var size = (Width: console.Profile.Width, Height: console.Profile.Height);
            if (size != renderedSize) redraw = true;
            if (renderView is not null && tableRenderer is not null && _clock.GetUtcNow() >= nextViewPoll)
            {
                if (tableRenderer.ViewRevision != renderedRevision || tableRenderer.RefreshDue) redraw = true;
                // Coalesce completions rather than clearing the screen for every DNS worker.
                nextViewPoll = _clock.GetUtcNow().AddMilliseconds(250);
            }

            if (redraw)
            {
                renderedRevision = tableRenderer?.ViewRevision ?? 0;
                if (renderView is not null)
                {
                    // The view owns clearing and rendering its breadcrumb, tables and header.
                    // Their old positions are invalid after a terminal resize.
                    // Menus without a table must not retain the previous view's DNS refresh scope.
                    tableRenderer?.ResetViewRefresh();
                    renderView();
                }
                else if (renderedLines > 0 && size == renderedSize)
                {
                    console.Cursor.MoveUp(renderedLines);
                    console.Write(new ControlCode("\r\x1b[0J"));
                }

                renderedLines = RenderMenu(console, title, options, selectedIndex, palette);
                renderedSize = size;
                redraw = false;
            }

            // Do not block in ReadKey: a resize must redraw even while the user is idle.
            if (!console.Input.IsKeyAvailable())
            {
                Thread.Sleep(50);
                continue;
            }

            var input = console.Input.ReadKey(true);
            if (input is not { } key) return null;

            if (key.Key is ConsoleKey.UpArrow)
            {
                selectedIndex = selectedIndex > 0 ? selectedIndex - 1 : options.Count - 1;
                redraw = true;
                continue;
            }

            if (key.Key is ConsoleKey.DownArrow)
            {
                selectedIndex = (selectedIndex + 1) % options.Count;
                redraw = true;
                continue;
            }

            if (key.Key == ConsoleKey.PageUp)
            {
                selectedIndex = Math.Max(0, selectedIndex - 10);
                redraw = true;
                continue;
            }

            if (key.Key == ConsoleKey.PageDown)
            {
                selectedIndex = Math.Min(options.Count - 1, selectedIndex + 10);
                redraw = true;
                continue;
            }

            if (key.Key == ConsoleKey.Home)
            {
                selectedIndex = 0;
                redraw = true;
                continue;
            }

            if (key.Key == ConsoleKey.End)
            {
                selectedIndex = options.Count - 1;
                redraw = true;
                continue;
            }

            if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar)
            {
                console.WriteLine();
                return options[selectedIndex];
            }

            if (key.Key == ConsoleKey.Escape)
            {
                console.WriteLine();
                return FindBackOrExit(options);
            }

            if (key.KeyChar != '\0' && shortcutMap.TryGetValue(char.ToLowerInvariant(key.KeyChar), out var selected))
            {
                console.WriteLine();
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

    private static int RenderMenu(IAnsiConsole console, string title, List<MenuEntry> options, int selectedIndex, UiThemePalette palette)
    {
        var lines = 0;

        lines += WriteMarkupLine(console, palette.AdaptMarkup(title));
        console.WriteLine();
        lines++;

        for (var i = 0; i < options.Count; i++)
        {
            var entry = options[i];
            var prefix = i == selectedIndex ? "> " : "  ";

            if (i == selectedIndex)
            {
                lines += WriteMarkupLine(console, $"{prefix}[{palette.Selection}]{Markup.Escape(Markup.Remove(entry.DisplayLabel))}[/]");
            }
            else
            {
                lines += WriteMarkupLine(console, $"{prefix}{palette.AdaptMarkup(entry.DisplayLabel)}");
            }
        }

        var hint = $"[{palette.Muted}]↑↓ Navigate | Shortcut key | Enter Confirm | Esc = Back[/]";
        lines += WriteMarkupLine(console, hint);

        return lines;
    }

    private static int WriteMarkupLine(IAnsiConsole console, string text)
    {
        var markup = new Markup(text);
        var renderOptions = RenderOptions.Create(console);
        var segments = ((IRenderable)markup).Render(renderOptions, Math.Max(1, console.Profile.Width));
        var lines = Math.Max(1, Segment.SplitLines(segments).Count);
        console.Write(markup);
        console.WriteLine();
        return lines;
    }
}
