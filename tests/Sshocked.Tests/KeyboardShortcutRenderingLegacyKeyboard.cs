using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class KeyboardShortcutRenderingLegacyKeyboard : IKeyboardShortcutService
{
    public int Calls { get; private set; }
    public MenuEntry? ShowMenu(string title, List<MenuEntry> options)
    {
        Calls++;
        return options[0];
    }
}
