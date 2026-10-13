using System.Globalization;
using Spectre.Console;

namespace Sshocked.Services;

public sealed class PathInputEditor(string initialValue = "")
{
    public string Text { get; private set; } = initialValue;
    public int Caret { get; private set; } = initialValue.Length;
    private int _viewStart;

    public void Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow: Caret = Previous(Caret); return;
            case ConsoleKey.RightArrow: Caret = Next(Caret); return;
            case ConsoleKey.Home: Caret = 0; return;
            case ConsoleKey.End: Caret = Text.Length; return;
            case ConsoleKey.Backspace when Caret > 0:
                var previous = Previous(Caret);
                Text = Text.Remove(previous, Caret - previous);
                Caret = previous;
                return;
            case ConsoleKey.Delete when Caret < Text.Length:
                Text = Text.Remove(Caret, Next(Caret) - Caret);
                return;
            case ConsoleKey.U when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                Text = string.Empty;
                Caret = 0;
                return;
        }
        if (key.KeyChar == '\0' || char.IsControl(key.KeyChar)) return;
        Text = Text.Insert(Caret, key.KeyChar.ToString());
        Caret++;
    }

    public PathInputViewport GetViewport(int width)
    {
        width = Math.Max(1, width);
        _viewStart = Math.Min(_viewStart, Caret);
        while (Text[_viewStart..Caret].GetCellWidth() >= width && _viewStart < Caret)
            _viewStart = Next(_viewStart);
        var end = _viewStart;
        while (end < Text.Length && Text[_viewStart..Next(end)].GetCellWidth() < width)
            end = Next(end);
        return new(Text[_viewStart..end], Text[_viewStart..Caret].GetCellWidth(), _viewStart);
    }

    public void ClickColumn(int column)
    {
        var index = _viewStart;
        var cell = 0;
        while (index < Text.Length)
        {
            var next = Next(index);
            var width = Text[index..next].GetCellWidth();
            if (cell + width > Math.Max(0, column)) break;
            cell += width;
            index = next;
        }
        Caret = index;
    }

    public static string Normalize(string value)
    {
        var path = value.Trim();
        // Explorer's "Copy as path" encloses the entire value; embedded quotes are not path delimiters.
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"') path = path[1..^1];
        return path;
    }

    private int Previous(int index) => StringInfo.ParseCombiningCharacters(Text)
        .Where(position => position < index).DefaultIfEmpty(0).Last();
    private int Next(int index) => StringInfo.ParseCombiningCharacters(Text)
        .Where(position => position > index).DefaultIfEmpty(Text.Length).First();
}
