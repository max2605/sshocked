using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MenuFactory(IKeyboardShortcutService keyboardShortcut, IUiChromeService chrome) : IMenuFactory
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
            await choice.ExecuteAsync();
        }

        return true;
    }
}
