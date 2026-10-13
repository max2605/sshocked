using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IMenuFactory
{
    Task<bool> RunMenu(string title, List<MenuEntry> entries);

    Task<bool> RunMenu(string title, List<MenuEntry> entries, Action renderView)
    {
        renderView();
        return RunMenu(title, entries);
    }
}
