using Spectre.Console;
using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class UiChromeService(IConfigRepository repository) : IUiChromeService, IDisposable
{
    private string? _appliedTheme;
    private readonly ConsoleColor _originalForeground = System.Console.ForegroundColor;
    private readonly ConsoleColor _originalBackground = System.Console.BackgroundColor;
    private readonly Color _originalAnsiForeground = AnsiConsole.Foreground;
    private readonly Color _originalAnsiBackground = AnsiConsole.Background;

    public void ApplyTheme()
    {
        var palette = UiThemePalette.For(repository.Load().UI.Theme);
        AnsiConsole.Foreground = palette.BodyStyle.Foreground;
        AnsiConsole.Background = palette.BodyStyle.Background;
        if (!AnsiConsole.Profile.Capabilities.Interactive || System.Console.IsOutputRedirected) return;
        System.Console.ForegroundColor = ConsoleColor.Gray;
        System.Console.BackgroundColor = ConsoleColor.Black;
        if (_appliedTheme == palette.Name) return;
        _appliedTheme = palette.Name;
        AnsiConsole.Clear();
    }

    public void RenderBreadcrumb(params string[] segments)
    {
        ApplyTheme();
        var ui = repository.Load().UI;
        if (!ui.ShowBreadcrumbs) return;
        var palette = UiThemePalette.For(ui.Theme);
        AnsiConsole.MarkupLine($"[{palette.Accent}]{string.Join($" [{palette.Muted}]>[/] ", segments.Select(Markup.Escape))}[/]");
        AnsiConsole.WriteLine();
    }

    public void RenderFooter()
    {
        ApplyTheme();
        var ui = repository.Load().UI;
        if (!ui.ShowFooter) return;
        var version = typeof(UiChromeService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        var color = UiThemePalette.For(ui.Theme).Muted;
        var text = $"ssk {version} · Config: {Path.Combine(ConfigPaths.DirectoryPath, "appconfig.json")} · Logs: {Path.Combine(ConfigPaths.DirectoryPath, "logs")}";
        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(text)}[/]");
    }

    public void Dispose()
    {
        AnsiConsole.Foreground = _originalAnsiForeground;
        AnsiConsole.Background = _originalAnsiBackground;
        if (_appliedTheme is null || System.Console.IsOutputRedirected) return;
        // Unix reports -1 until an explicit console color was set; reset its terminal defaults.
        System.Console.ResetColor();
        if (Enum.IsDefined(_originalForeground)) System.Console.ForegroundColor = _originalForeground;
        if (Enum.IsDefined(_originalBackground)) System.Console.BackgroundColor = _originalBackground;
    }
}
