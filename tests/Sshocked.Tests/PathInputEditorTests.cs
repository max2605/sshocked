using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class PathInputEditorTests
{
    [Theory]
    [InlineData("  \"C:\\folder with spaces\\key.pem\"  ", "C:\\folder with spaces\\key.pem")]
    [InlineData("\"\\\\server\\share\\file.txt\"", "\\\\server\\share\\file.txt")]
    [InlineData("~/identity", "~/identity")]
    [InlineData("\"unfinished", "\"unfinished")]
    [InlineData("\"\"", "")]
    public void NormalizationRemovesBalancedWindowsCopyAsPathQuotes(string input, string expected)
        => Assert.Equal(expected, PathInputEditor.Normalize(input));

    [Fact]
    public void CaretNavigationAllowsInsertionDeletionAndClearing()
    {
        var editor = new PathInputEditor("C:\\old\\key.pem");
        editor.Handle(Key(ConsoleKey.Home));
        for (var index = 0; index < 3; index++) editor.Handle(Key(ConsoleKey.RightArrow));
        editor.Handle(Key(ConsoleKey.Delete));
        editor.Handle(Key(ConsoleKey.N, 'n'));
        Assert.Equal("C:\\nld\\key.pem", editor.Text);
        editor.Handle(Key(ConsoleKey.LeftArrow));
        editor.Handle(Key(ConsoleKey.Backspace));
        Assert.Equal("C:nld\\key.pem", editor.Text);
        editor.Handle(Key(ConsoleKey.End));
        Assert.Equal(editor.Text.Length, editor.Caret);
        editor.Handle(new('\u0015', ConsoleKey.U, false, false, true));
        Assert.Equal("", editor.Text);
        Assert.Equal(0, editor.Caret);
    }

    [Fact]
    public void ClickingAfterHorizontalScrollPositionsCaretInOriginalPath()
    {
        var editor = new PathInputEditor("C:\\very long folder\\identity.pem");
        var viewport = editor.GetViewport(10);
        Assert.True(viewport.Start > 0);
        editor.ClickColumn(2);
        Assert.Equal(viewport.Start + 2, editor.Caret);
        editor.Handle(Key(ConsoleKey.X, 'X'));
        Assert.Equal('X', editor.Text[viewport.Start + 2]);
    }

    [Fact]
    public void NavigationAndMouseColumnsRespectWideAndCombinedUnicodeCharacters()
    {
        var editor = new PathInputEditor("C:\\文e\u0301😀");
        editor.Handle(Key(ConsoleKey.LeftArrow));
        Assert.Equal(6, editor.Caret);
        editor.Handle(Key(ConsoleKey.Backspace));
        Assert.Equal("C:\\文😀", editor.Text);
        editor.Handle(Key(ConsoleKey.Home));
        editor.GetViewport(20);
        editor.ClickColumn(5);
        Assert.Equal(4, editor.Caret);
    }

    [Fact]
    public void NativeWindowsRecordsDecodeMouseCoordinatesAndRepeatedKeys()
    {
        var mouse = new byte[20];
        BitConverter.GetBytes((ushort)2).CopyTo(mouse, 0);
        BitConverter.GetBytes((short)14).CopyTo(mouse, 4);
        BitConverter.GetBytes((short)7).CopyTo(mouse, 6);
        BitConverter.GetBytes((uint)1).CopyTo(mouse, 8);
        Assert.Equal(new PathInputEvent(Column: 14, Row: 7), PathInputEvent.DecodeWindowsRecord(mouse));
        var key = new byte[20];
        BitConverter.GetBytes((ushort)1).CopyTo(key, 0);
        BitConverter.GetBytes(1).CopyTo(key, 4);
        BitConverter.GetBytes((ushort)3).CopyTo(key, 8);
        BitConverter.GetBytes((ushort)ConsoleKey.LeftArrow).CopyTo(key, 10);
        Assert.Equal(new PathInputEvent(Key(ConsoleKey.LeftArrow), Repeat: 3), PathInputEvent.DecodeWindowsRecord(key));
        BitConverter.GetBytes(0).CopyTo(key, 4);
        Assert.Null(PathInputEvent.DecodeWindowsRecord(key));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char value = '\0') => new(value, key, false, false, false);
}
