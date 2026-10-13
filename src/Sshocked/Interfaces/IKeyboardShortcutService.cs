using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IKeyboardShortcutService
{
    MenuEntry? ShowMenu(string title, List<MenuEntry> options);

    MenuEntry? ShowMenu(string title, List<MenuEntry> options, Action renderView)
    {
        renderView();
        return ShowMenu(title, options);
    }
}
