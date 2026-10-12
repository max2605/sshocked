using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ConsoleHelperService(ISshRunnerService sshRunner, IDockerService dockerService, IRecentConnectionService recents) : IConsoleHelperService
{
    public void WaitForKey()
    {
        AnsiConsole.MarkupLine("Press any key to continue...");
        System.Console.ReadKey(true);
    }

    public async Task RunSsh(ServerHost host)
    {
        var wasInteractive = AnsiConsole.Console.Profile.Capabilities.Interactive;
        AnsiConsole.Console.Profile.Capabilities.Interactive = false;
        System.Console.ResetColor();

        try { await sshRunner.Connect(host); }
        finally
        {
            System.Console.ResetColor();
            AnsiConsole.Console.Profile.Capabilities.Interactive = wasInteractive;
        }
    }

    public async Task RunDockerExec(ServerHost host, ContainerModel container)
    {
        var wasInteractive = AnsiConsole.Console.Profile.Capabilities.Interactive;
        AnsiConsole.Console.Profile.Capabilities.Interactive = false;
        System.Console.ResetColor();

        try
        {
            await dockerService.Exec(host, container);
            recents.Record(host, container);
        }
        finally
        {
            System.Console.ResetColor();
            AnsiConsole.Console.Profile.Capabilities.Interactive = wasInteractive;
        }
    }

    public async Task RunDockerLogs(ServerHost host, ContainerModel container)
    {
        var wasInteractive = AnsiConsole.Console.Profile.Capabilities.Interactive;
        AnsiConsole.Console.Profile.Capabilities.Interactive = false;
        System.Console.ResetColor();

        try { await dockerService.Logs(host, container); }
        finally
        {
            System.Console.ResetColor();
            AnsiConsole.Console.Profile.Capabilities.Interactive = wasInteractive;
        }
    }
}
