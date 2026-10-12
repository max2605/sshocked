using Spectre.Console;

using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class ConsoleHelperTests
{
    [Theory]
    [InlineData("ssh", true, false)]
    [InlineData("ssh", true, true)]
    [InlineData("ssh", false, true)]
    [InlineData("exec", true, false)]
    [InlineData("exec", true, true)]
    [InlineData("exec", false, true)]
    [InlineData("logs", true, false)]
    [InlineData("logs", true, true)]
    [InlineData("logs", false, true)]
    public async Task SessionCompletionAndFailureRestorePreviousConsoleCapabilities(string operation, bool wasInteractive, bool fails)
    {
        var previous = AnsiConsole.Console;
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(TextWriter.Null) });
        var recents = new ConsoleHelperTestsRecents();
        try
        {
            AnsiConsole.Console = console;
            console.Profile.Capabilities.Interactive = wasInteractive;
            var session = new ConsoleHelperTestsSession(fails);
            var service = new ConsoleHelperService(session, session, recents);
            // No cached password/reference: interactive Docker operations must not prompt locally.
            var host = new ServerHost();
            var container = new ContainerModel
            {
                Name = "app"
            };
            Task Run() => operation switch
            {
                "ssh" => service.RunSsh(host),
                "exec" => service.RunDockerExec(host, container),
                _ => service.RunDockerLogs(host, container)
            };
            if (fails)
                await Assert.ThrowsAsync<IOException>(Run);
            else
                await Run();
            Assert.Equal(wasInteractive, console.Profile.Capabilities.Interactive);
            Assert.Equal(operation == "exec" && !fails ? 1 : 0, recents.Count);
            Assert.Null(host.SudoPassword);
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }
}
