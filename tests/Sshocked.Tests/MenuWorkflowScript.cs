using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class MenuWorkflowScript : IMenuFactory
{
    private readonly Queue<(string Choice, Action? After)> steps = new();
    public List<(string Title, List<MenuEntry> Entries)> Menus { get; } = [];

    public void Choose(string label, Action? after = null) => steps.Enqueue((label, after));

    public async Task<bool> RunMenu(string title, List<MenuEntry> entries)
    {
        Menus.Add((title, entries));
        if (!steps.TryDequeue(out var step)) throw new InvalidOperationException("Unexpected menu: " + title);
        var choice = entries.FirstOrDefault(entry => entry.Label.Contains(step.Choice, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Choice '{step.Choice}' is missing in {title}");
        if (choice.ActionType is MenuActionType.Back or MenuActionType.Exit) return false;
        if (choice.ExecuteAsync is not null) await choice.ExecuteAsync();
        step.After?.Invoke();
        return true;
    }
}
