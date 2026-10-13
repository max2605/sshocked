using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ICommandPaletteService
{
    MenuEntry? Show(IReadOnlyList<MenuEntry> current);
    void ShowHelp(IReadOnlyList<MenuEntry> current);
}
