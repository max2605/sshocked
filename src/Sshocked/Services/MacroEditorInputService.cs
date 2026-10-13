using Spectre.Console;
using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class MacroEditorInputService(IConsoleWriterService console) : IMacroEditorInputService
{
    public string? Prompt(string label, string? initialValue = null)
    {
        console.WriteLine(label + (string.IsNullOrEmpty(initialValue)
            ? " (empty or Esc cancels):" : " (Esc cancels; Enter keeps the current value):"));
        var interactive = AnsiConsole.Profile.Capabilities.Interactive &&
            !System.Console.IsInputRedirected && !System.Console.IsOutputRedirected;
        var multiline = initialValue?.IndexOfAny(['\r', '\n']) >= 0;
        if ((!interactive || multiline) && !string.IsNullOrEmpty(initialValue))
            console.WriteLine("Current value:\n" + initialValue);
        string? value;
        if (interactive)
        {
            console.WriteLine("←/→ or Home/End move the caret; Delete/Backspace edit; Ctrl+U clears.");
            value = ReadEditable(multiline ? string.Empty : initialValue ?? string.Empty);
        }
        else { console.Write("> "); value = console.ReadLine(); }
        if (value is null) return null;
        if (value.Length == 0) return string.IsNullOrEmpty(initialValue) ? null : initialValue;
        return value;
    }

    private string? ReadEditable(string initialValue)
    {
        var editor = new PathInputEditor(initialValue);
        using var windows = WindowsPathInputReader.TryCreate();
        var row = System.Console.CursorTop;
        while (true)
        {
            var width = Math.Max(1, System.Console.WindowWidth - 3);
            var view = editor.GetViewport(width);
            System.Console.SetCursorPosition(0, row);
            console.Write("> " + view.Text + new string(' ', width - view.Text.GetCellWidth()));
            row = System.Console.CursorTop;
            System.Console.SetCursorPosition(2 + view.CaretColumn, row);
            var input = windows?.Read() ?? new PathInputEvent(System.Console.ReadKey(true));
            if (input.Row == row && input.Column is { } column) { editor.ClickColumn(column - 2); continue; }
            if (input.Key is not { } key) continue;
            if (key.Key is ConsoleKey.Escape or ConsoleKey.Enter)
            {
                System.Console.SetCursorPosition(0, row);
                console.WriteLine("> " + editor.Text);
                return key.Key == ConsoleKey.Escape ? null : editor.Text;
            }
            for (var repeat = 0; repeat < input.Repeat; repeat++) editor.Handle(key);
        }
    }
}
