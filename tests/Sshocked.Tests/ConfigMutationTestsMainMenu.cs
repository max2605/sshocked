using Sshocked.Interfaces;



namespace Sshocked.Tests;

internal sealed class ConfigMutationTestsMainMenu : IMainMenuService
{
    public bool Shown { get; private set; }

    public Task Show()
    {
        Shown = true;
        return Task.CompletedTask;
    }
}
