using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MenuFactory(IKeyboardShortcutService keyboardShortcut, IUiChromeService chrome, IConsoleHelperService console,
    IConfigRepository repository, ILogger<MenuFactory> logger) : IMenuFactory
{
    public async Task<bool> RunMenu(string title, List<MenuEntry> entries)
    {
        chrome.RenderFooter();
        var choice = keyboardShortcut.ShowMenu(title, entries);

        return await ExecuteChoice(choice);
    }

    public async Task<bool> RunMenu(string title, List<MenuEntry> entries, Action renderView)
    {
        var choice = keyboardShortcut.ShowMenu(title, entries, () =>
        {
            renderView();
            chrome.RenderFooter();
        });

        return await ExecuteChoice(choice);
    }

    private async Task<bool> ExecuteChoice(MenuEntry? choice)
    {
        if (choice?.ActionType == MenuActionType.Exit || choice?.ActionType == MenuActionType.Back)
        {
            return false;
        }

        if (choice?.ExecuteAsync != null)
        {
            try { await choice.ExecuteAsync(); }
            catch (Exception error)
            {
                logger.LogError(error, "Menu action failed");
                var palette = UiThemePalette.For(repository.Load().UI.Theme);
                AnsiConsole.MarkupLine($"[{palette.Error}]Error: {Markup.Escape(AuditRedactor.Redact(error.Message) ?? "Action failed")}[/]");
                if (AnsiConsole.Profile.Capabilities.Interactive) console.WaitForKey();
            }
        }

        return true;
    }
}
