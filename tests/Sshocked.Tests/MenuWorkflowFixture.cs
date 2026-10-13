using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Logging;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

internal sealed class MenuWorkflowFixture : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-menu-tests-" + Guid.NewGuid().ToString("N"));
    private readonly IAnsiConsole previousConsole = AnsiConsole.Console;
    private readonly FileLoggerProcessor processor;
    public ServiceProvider Provider { get; }
    public JsonConfigRepository Repository { get; }
    public MenuWorkflowScript Script { get; } = new();
    public MenuWorkflowConsole ConsoleHelper { get; } = new();
    public MenuWorkflowTargets Targets { get; } = new();
    public INavigationService Navigation => Provider.GetRequiredService<INavigationService>();

    public MenuWorkflowFixture(AppConfig config, string input = "")
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(TextWriter.Null), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.Yes });
        console.Profile.Capabilities.Interactive = true;
        AnsiConsole.Console = new ContainerMutationTestsPromptConsole(console, new ContainerMutationTestsPromptInput(input, () => { }));
        var services = new ServiceCollection();
        processor = new FileLoggerProcessor(directory);
        services.AddSingleton(processor);
        ServiceRegistration.Configure(services);
        Repository = new JsonConfigRepository(Path.Combine(directory, "appconfig.json"));
        config.UI.EnableReachabilityChecks = false;
        Repository.Save(config);
        services.AddSingleton<IConfigRepository>(Repository);
        services.AddSingleton<ILocalCredentialService>(new LocalCredentialService(Path.Combine(directory, "credentials")));
        services.AddSingleton(new SessionRepository(Path.Combine(directory, "sessions")));
        services.AddSingleton<IMenuFactory>(Script);
        services.AddSingleton<IConsoleHelperService>(ConsoleHelper);
        services.AddSingleton<IGroupSessionService>(Targets);
        services.AddSingleton<IMacroMenuService>(Targets);
        services.AddSingleton<IManagedCommandSessionService>(Targets);
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public async ValueTask DisposeAsync()
    {
        try { await Provider.DisposeAsync(); }
        finally
        {
            processor.Dispose();
            AnsiConsole.Console = previousConsole;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
