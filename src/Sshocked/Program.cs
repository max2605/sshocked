using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Services;

// OpenSSH invokes this executable as an askpass helper with a prompt argument.
// Resolve it before configuration, DI and logging so only the requested password reaches stdout.
if (Environment.GetEnvironmentVariable("SSHOCKED_INTERNAL_ASKPASS") == "1")
{
    Console.WriteLine(Environment.GetEnvironmentVariable("SSHOCKED_ASKPASS_PASSWORD") ?? "");
    return;
}

var services = new ServiceCollection();
ConfigureServices(services);

var serviceProvider = services.BuildServiceProvider();

AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    var logger = serviceProvider.GetService<ILogger<Program>>();
    var ex = args.ExceptionObject as Exception;
    logger?.LogCritical(ex, "Unhandled application exception");
};

var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Application starting");

try
{
    var argumentParser = serviceProvider.GetRequiredService<IArgumentParserService>();
    var parseResult = argumentParser.Parse(args);

    if (!parseResult.IsInteractive)
    {
        var dispatcher = serviceProvider.GetRequiredService<ICliDispatcherService>();
        await dispatcher.Dispatch(parseResult);
        logger.LogInformation("CLI dispatch complete");
        return;
    }

    _ = AnsiConsole.Profile.Capabilities;

    var appRunner = serviceProvider.GetRequiredService<IAppRunner>();
    await appRunner.Run();
    logger.LogInformation("Application shutdown complete");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Application terminated with unhandled exception");
    throw;
}
finally
{
    if (serviceProvider is IAsyncDisposable asyncDisposable)
    {
        await asyncDisposable.DisposeAsync();
    }
    else if (serviceProvider is IDisposable disposable)
    {
        disposable.Dispose();
    }
}

static void ConfigureServices(IServiceCollection services)
{
    services.AddLogging(builder =>
    {
        builder.AddFileLogger();
        builder.SetMinimumLevel(LogLevel.Trace);
    });

    services.AddSingleton<IConfigRepository, JsonConfigRepository>();
    services.AddSingleton<IRecentConnectionService, RecentConnectionService>();
    services.AddSingleton<IAuditService, AuditService>();
    services.AddSingleton<IHookExecutorService, HookExecutorService>();
    services.AddSingleton<IManagementCommandHandler, AuditCommandService>();
    services.AddSingleton<IQuickAccessMenuService, QuickAccessMenuService>();
    services.AddSingleton<ISshConfigImporter, SshConfigImporter>();
    services.AddSingleton<ISshConfigExporter, SshConfigExporter>();
    services.AddSingleton<ISshConfigWatcher, SshConfigWatcher>();
    services.AddSingleton<ISshConfigSyncService, SshConfigSyncService>();
    services.AddSingleton<ISshConfigSyncMenuService, SshConfigSyncMenuService>();
    services.AddSingleton<ISshKeyDiscoveryService, SshKeyDiscoveryService>();
    services.AddSingleton<ISshKeyPickerService, SshKeyPickerService>();
    services.AddSingleton<IConsoleWriterService, ConsoleWriterService>();
    services.AddSingleton<IGroupManagementService, GroupManagementService>();
    services.AddSingleton<IServerCrudService, ServerCrudService>();
    services.AddSingleton<ISshRunnerService, SshRunnerService>();
    services.AddSingleton<IGroupConnectionService, GroupConnectionService>();
    services.AddSingleton<ITerminalLauncherService, TerminalLauncherService>();
    services.AddSingleton<IExternalProcessRunner, ExternalProcessRunner>();
    services.AddSingleton<IBastionService, AzureBastionService>();
    services.AddSingleton<ISecretProviderFactory, AzureKeyVaultSecretProviderFactory>();
    services.AddSingleton<ISecretProviderRegistry, SecretProviderRegistry>();
    services.AddSingleton<ISecretConfigurationUi, SecretConfigurationUi>();
    services.AddSingleton<IFileTransferService, FileTransferService>();
    services.AddSingleton<FileTransferUi>();
    services.AddSingleton<IDockerService, DockerService>();
    services.AddSingleton<IContainerService, ContainerService>();
    services.AddSingleton<IContainerGroupService, ContainerGroupService>();
    services.AddSingleton<INavigationService, NavigationService>();
    services.AddSingleton<IProcessService, ProcessService>();
    services.AddSingleton<IGroupMenuService, GroupMenuService>();
    services.AddSingleton<IServerMenuService, ServerMenuService>();
    services.AddSingleton<IMainMenuService, MainMenuService>();
    services.AddSingleton<ITableRendererService, TableRendererService>();
    services.AddSingleton<IHostSelectorService, HostSelectorService>();
    services.AddSingleton<IConsoleHelperService, ConsoleHelperService>();
    services.AddSingleton<IKeyboardShortcutService, KeyboardShortcutService>();
    services.AddSingleton<IMenuFactory, MenuFactory>();
    services.AddSingleton<IArgumentParserService, ArgumentParserService>();
    services.AddSingleton<ICliDispatcherService, CliDispatcherService>();
    services.AddTransient<IAppRunner, AppRunner>();
}
