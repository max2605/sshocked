using System.Text;
using Spectre.Console;

namespace Sshocked.Services;

/// <summary>Measures the visible terminal rather than its potentially wider scroll buffer.</summary>
internal sealed class TerminalConsoleOutput : IAnsiConsoleOutput
{
    private readonly IAnsiConsoleOutput _output;
    private readonly Func<int> _windowWidth;
    private readonly Func<int> _windowHeight;
    private readonly bool _reserveLastColumn;

    public TerminalConsoleOutput()
        : this(new AnsiConsoleOutput(System.Console.Out),
            () => System.Console.WindowWidth, () => System.Console.WindowHeight,
            OperatingSystem.IsWindows())
    {
    }

    internal TerminalConsoleOutput(IAnsiConsoleOutput output, Func<int> windowWidth,
        Func<int> windowHeight, bool reserveLastColumn)
    {
        _output = output;
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;
        _reserveLastColumn = reserveLastColumn;
    }

    public TextWriter Writer => _output.Writer;
    public bool IsTerminal => _output.IsTerminal;

    // Do not pin Profile.Width: this getter must follow every subsequent window resize.
    // Leaving the last Windows cell unused prevents automatic wrapping followed by reflow
    // from joining table rows when the console is maximized or restored.
    public int Width
    {
        get
        {
            var terminal = IsTerminal;
            var fallback = ReadSize(() => _output.Width, 80);
            var width = terminal ? ReadSize(_windowWidth, fallback) : fallback;
            return Math.Max(1, width - (terminal && _reserveLastColumn ? 1 : 0));
        }
    }

    public int Height
    {
        get
        {
            var fallback = ReadSize(() => _output.Height, 25);
            return IsTerminal ? ReadSize(_windowHeight, fallback) : fallback;
        }
    }

    public void SetEncoding(Encoding encoding) => _output.SetEncoding(encoding);

    private static int ReadSize(Func<int> read, int fallback)
    {
        try
        {
            var size = read();
            return size > 0 ? size : fallback;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            return fallback;
        }
    }
}
