using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MenuFactory(IKeyboardShortcutService keyboardShortcut, IUiChromeService chrome, IConsoleHelperService console, ILogger<MenuFactory> logger) : IMenuFactory
{
    public async Task<bool> RunMenu(string title, List<MenuEntry> entries)
    {
        chrome.RenderFooter();
        var choice = keyboardShortcut.ShowMenu(title, entries);

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
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(AuditService.Redact(error.Message) ?? "Action failed")}[/]");
                if (AnsiConsole.Profile.Capabilities.Interactive) console.WaitForKey();
            }
        }

        return true;
    }
}
