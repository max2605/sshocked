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
        var text = $"ssk {version} · Config: {Path.Combine(ConfigPaths.DirectoryPath, "appconfig.json")} · Logs: {Path.Combine(ConfigPaths.DirectoryPath, "logs")}";
        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(text)}[/]");
    }
}
