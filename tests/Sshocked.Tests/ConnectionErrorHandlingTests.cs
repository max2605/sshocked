using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class ConnectionErrorHandlingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CertificateFailureDisplaysGuidanceAndReturnsToMenu(bool interactive)
    {
        var previous = AnsiConsole.Console;
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        console.Profile.Width = 1000;
        console.Profile.Capabilities.Interactive = interactive;
        var error = new AzureCliCertificateException("Azure Bastion tunnel", 1, "[SSL: CERTIFICATE_VERIFY_FAILED] unable to get local issuer certificate");
        var logger = new CaptureLogger();
        var menuConsole = new MenuWorkflowConsole();
        var sessions = new ConsoleHelperService(new FailingSshRunner(error), new ConsoleHelperTestsSession(false), new ConsoleHelperTestsRecents());
        var menu = new MenuFactory(new FirstChoiceKeyboard(), new KeyboardShortcutRenderingChrome(), menuConsole,
            new TableRendererTestsMemoryRepository(new()), logger);
        try
        {
            AnsiConsole.Console = console;

            var continuing = await menu.RunMenu("Servers", [new("Connect", executeAsync: () => sessions.RunSsh(new ServerHost()))]);

            Assert.True(continuing);
            Assert.Equal(interactive, console.Profile.Capabilities.Interactive);
            Assert.Equal(interactive ? 1 : 0, menuConsole.Waits);
            Assert.Contains(error.Message, output.ToString());
            Assert.Contains("REQUESTS_CA_BUNDLE", output.ToString());
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Null(entry.Exception);
            Assert.Equal(error.Message, entry.Message);
        }
        finally { AnsiConsole.Console = previous; }
    }

    [Fact]
    public async Task UnexpectedFailureRetainsErrorLoggingAndEscapesRedactedMessage()
    {
        var previous = AnsiConsole.Console;
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        console.Profile.Width = 1000;
        console.Profile.Capabilities.Interactive = false;
        var error = new InvalidOperationException("Unexpected [problem] --token=secret-value");
        var logger = new CaptureLogger();
        var menu = new MenuFactory(new FirstChoiceKeyboard(), new KeyboardShortcutRenderingChrome(), new MenuWorkflowConsole(),
            new TableRendererTestsMemoryRepository(new()), logger);
        try
        {
            AnsiConsole.Console = console;

            var continuing = await menu.RunMenu("Servers", [new("Connect", executeAsync: () => Task.FromException(error))]);

            Assert.True(continuing);
            Assert.Contains("Unexpected [problem]", output.ToString());
            Assert.Contains("[REDACTED]", output.ToString());
            Assert.DoesNotContain("secret-value", output.ToString());
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Same(error, entry.Exception);
        }
        finally { AnsiConsole.Console = previous; }
    }

    private sealed class FirstChoiceKeyboard : IKeyboardShortcutService
    {
        public MenuEntry? ShowMenu(string title, List<MenuEntry> options) => options[0];
    }

    private sealed class FailingSshRunner(Exception error) : ISshRunnerService
    {
        public Task Connect(ServerHost host) => Task.FromException(error);
    }

    private sealed class CaptureLogger : ILogger<MenuFactory>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
