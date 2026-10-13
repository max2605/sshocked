using Spectre.Console;
using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class PathInputService(IConsoleWriterService console) : IPathInputService
{
    public string? Prompt(string label, string? defaultValue = null, bool allowEmpty = true,
        Func<string, string?>? validate = null)
    {
        console.WriteLine(label + (allowEmpty ? " (empty or Esc cancels):" : ":"));
        var interactive = AnsiConsole.Profile.Capabilities.Interactive && !System.Console.IsInputRedirected && !System.Console.IsOutputRedirected;
        if (interactive) console.WriteLine("←/→ or Home/End move the caret · Delete/Backspace edit · Ctrl+U clears · Windows: click to position");
        var value = defaultValue ?? string.Empty;
        while (true)
        {
            var input = interactive ? ReadEditable(value) : ReadRedirected();
            if (input is null) return null;
            value = PathInputEditor.Normalize(input);
            if (string.IsNullOrWhiteSpace(value) && allowEmpty) return null;
            var error = value.IndexOfAny(['\0', '\r', '\n']) >= 0 ? "Paths must not contain line breaks or NUL."
                : string.IsNullOrWhiteSpace(value) ? "Enter a path."
                : validate?.Invoke(value);
            if (error is null) return value;
            console.WriteLine(error);
        }
    }

    private string? ReadRedirected()
    {
        console.Write("> ");
        return console.ReadLine();
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
            if (input.Row == row && input.Column is { } column)
            {
                editor.ClickColumn(column - 2);
                continue;
            }
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
