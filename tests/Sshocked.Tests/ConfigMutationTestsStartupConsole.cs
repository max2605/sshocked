using Sshocked.Interfaces;



namespace Sshocked.Tests;

internal sealed class ConfigMutationTestsStartupConsole(Action onWelcome) : IConsoleWriterService
{
    public void WriteLine(string message)
    {
        if (message.StartsWith("Welcome", StringComparison.Ordinal))
            onWelcome();
    }

    public void Write(string message)
    {
    }

    public string? ReadLine() => "n";
}
