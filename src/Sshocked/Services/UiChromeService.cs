using Spectre.Console;
using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class UiChromeService(IConfigRepository repository) : IUiChromeService
{
    public void RenderBreadcrumb(params string[] segments)
    {
        var ui = repository.Load().UI;
        if (!ui.ShowBreadcrumbs) return;
        var color = ui.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "blue" : "cyan";
        AnsiConsole.MarkupLine($"[{color}]{string.Join(" [grey]>[/] ", segments.Select(Markup.Escape))}[/]");
        AnsiConsole.WriteLine();
    }

    public void RenderFooter()
    {
        var ui = repository.Load().UI;
        if (!ui.ShowFooter) return;
        var version = typeof(UiChromeService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        var color = ui.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "grey35" : "grey";
        // FileLoggerProcessor uses the platform application directory independently of config overrides.
        var logBase = OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        var text = $"ssk {version} · Config: {Path.Combine(ConfigPaths.DirectoryPath, "appconfig.json")} · Logs: {Path.Combine(logBase, "sshocked", "logs")}";
        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(text)}[/]");
    }
}
