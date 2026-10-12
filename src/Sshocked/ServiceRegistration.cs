using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sshocked.Interfaces;
using Sshocked.Services;

namespace Sshocked;

internal static class ServiceRegistration
{
    public static void Configure(IServiceCollection services)
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
        services.AddSingleton<SessionRepository>();
        services.AddSingleton<ISessionRecordingService, SessionRecordingService>();
        services.AddSingleton<IManagementCommandHandler, SessionCommandHandler>();
        services.AddSingleton<SessionRecordingConfigurationUi>();
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
        services.AddSingleton<IServerFormService, ServerFormService>();
        services.AddSingleton<ISettingsMenuService, SettingsMenuService>();
        services.AddSingleton<IQuickConnectionMenuService, QuickConnectionMenuService>();
        services.AddSingleton<IGroupSessionService, GroupSessionService>();
        services.AddSingleton<IContainerMenuService, ContainerMenuService>();
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
        services.AddSingleton<IHostReachabilityService, HostReachabilityService>();
        services.AddSingleton<IUiChromeService, UiChromeService>();
        services.AddSingleton<IUiSettingsMenuService, UiSettingsMenuService>();
        services.AddSingleton<IConsoleHelperService, ConsoleHelperService>();
        services.AddSingleton<IKeyboardShortcutService, KeyboardShortcutService>();
        services.AddSingleton<IMenuFactory, MenuFactory>();
        services.AddSingleton<IArgumentParserService, ArgumentParserService>();
        services.AddSingleton<ICliDispatcherService, CliDispatcherService>();
        services.AddTransient<IAppRunner, AppRunner>();
    }
}
