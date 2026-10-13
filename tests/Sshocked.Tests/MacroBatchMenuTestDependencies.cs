using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class MacroBatchMenuTestDependencies : IBatchMenuService
{
    public Action? BeforePreview { get; set; }
    public IReadOnlyList<ServerHost> Targets { get; private set; } = [];
    public MacroDefinition? Macro { get; private set; }

    public Task RunMacro(Func<IReadOnlyList<ServerHost>> targets)
    {
        BeforePreview?.Invoke();
        Targets = targets();
        return Task.CompletedTask;
    }

    public Task RunMacro(Func<IReadOnlyList<ServerHost>> targets, MacroDefinition macro)
    {
        Macro = macro;
        return RunMacro(targets);
    }

    public Task Show() => throw new InvalidOperationException("Unexpected batch menu.");
    public Task RunCommand(Func<IReadOnlyList<ServerHost>> targets) => throw new InvalidOperationException("Unexpected command batch.");
    public Task Upload(Func<IReadOnlyList<ServerHost>> targets) => throw new InvalidOperationException("Unexpected upload.");
    public Task Download(Func<IReadOnlyList<ServerHost>> targets) => throw new InvalidOperationException("Unexpected download.");
}
