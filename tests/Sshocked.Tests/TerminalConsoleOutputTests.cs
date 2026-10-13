using System.Text;
using Spectre.Console;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class TerminalConsoleOutputTests
{
    [Fact]
    public void ConsoleProfileFollowsViewportWhenScrollBufferIsWiderAndWindowResizes()
    {
        var viewportWidth = 120;
        var viewportHeight = 30;
        var output = new MemoryOutput { Width = 400, Height = 9000 };
        var terminal = new TerminalConsoleOutput(output, () => viewportWidth, () => viewportHeight, true);
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = terminal, Ansi = AnsiSupport.No });

        Assert.Equal(119, console.Profile.Width);
        Assert.Equal(30, console.Profile.Height);
        viewportWidth = 240;
        viewportHeight = 60;
        Assert.Equal(239, console.Profile.Width);
        Assert.Equal(60, console.Profile.Height);
        viewportWidth = 120;
        Assert.Equal(119, console.Profile.Width);
    }

    [Fact]
    public void RedirectedOutputRetainsReportedSizeWithoutReadingWindow()
    {
        var output = new MemoryOutput { IsTerminal = false, Width = 200, Height = 45 };
        var terminal = new TerminalConsoleOutput(output,
            () => throw new Exception("Window width should not be accessed"),
            () => throw new Exception("Window height should not be accessed"), true);

        Assert.False(terminal.IsTerminal);
        Assert.Equal(200, terminal.Width);
        Assert.Equal(45, terminal.Height);
        Assert.Same(output.Writer, terminal.Writer);
    }

    [Theory]
    [InlineData(false, 120)]
    [InlineData(true, 119)]
    public void LastColumnMarginIsLimitedToWindowsTerminals(bool reserveLastColumn, int expected)
    {
        var terminal = new TerminalConsoleOutput(new MemoryOutput(), () => 120, () => 30, reserveLastColumn);
        Assert.Equal(expected, terminal.Width);
    }

    [Fact]
    public void UnsupportedWindowDimensionsFallBackToPositiveOutputDimensions()
    {
        var terminal = new TerminalConsoleOutput(new MemoryOutput { Width = 120, Height = 30 },
            () => throw new PlatformNotSupportedException(), () => throw new IOException(), true);
        Assert.Equal(119, terminal.Width);
        Assert.Equal(30, terminal.Height);
    }

    [Fact]
    public void InvalidWindowDimensionsAndTinyTerminalsRemainPositive()
    {
        var terminal = new TerminalConsoleOutput(new MemoryOutput { Width = 80, Height = 25 },
            () => 0, () => -1, true);
        Assert.Equal(79, terminal.Width);
        Assert.Equal(25, terminal.Height);
        terminal = new TerminalConsoleOutput(new MemoryOutput(), () => 1, () => 1, true);
        Assert.Equal(1, terminal.Width);
        Assert.Equal(1, terminal.Height);
    }

    [Fact]
    public void EncodingChangesReachUnderlyingOutput()
    {
        var output = new MemoryOutput();
        var terminal = new TerminalConsoleOutput(output, () => 120, () => 30, true);
        var encoding = new UTF8Encoding(false);
        terminal.SetEncoding(encoding);
        Assert.Same(encoding, output.Encoding);
    }

    private sealed class MemoryOutput : IAnsiConsoleOutput
    {
        public TextWriter Writer { get; } = new StringWriter();
        public bool IsTerminal { get; set; } = true;
        public int Width { get; set; } = 400;
        public int Height { get; set; } = 9000;
        public Encoding? Encoding { get; private set; }
        public void SetEncoding(Encoding encoding) => Encoding = encoding;
    }
}
