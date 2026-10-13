using Sshocked.Models;

namespace Sshocked.Services;

public sealed class CommandPaletteCatalog
{
    private Func<IReadOnlyList<MenuEntry>> _global = () => [];
    public void Register(Func<IReadOnlyList<MenuEntry>> global) => _global = global;
    public IReadOnlyList<MenuEntry> Global() => _global();
}
