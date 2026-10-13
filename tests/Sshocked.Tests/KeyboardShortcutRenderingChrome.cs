using Spectre.Console;
using Sshocked.Interfaces;

namespace Sshocked.Tests;

internal sealed class KeyboardShortcutRenderingChrome : IUiChromeService
{
    public int Footers { get; private set; }
    public void ApplyTheme() { }
    public void RenderBreadcrumb(params string[] segments) { }
    public void RenderFooter()
    {
        Footers++;
        AnsiConsole.MarkupLine("Footer marker");
    }
}
