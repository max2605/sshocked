using Microsoft.Extensions.DependencyInjection;
using Sshocked.Interfaces;
using Sshocked.Logging;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public async Task RefactoredMenusAndApplicationResolveFromTheProductionContainer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-di-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            using var processor = new FileLoggerProcessor(directory);
            services.AddSingleton(processor);
            ServiceRegistration.Configure(services);
            services.AddSingleton<IConfigRepository>(new JsonConfigRepository(Path.Combine(directory, "appconfig.json")));
            services.AddSingleton(new SessionRepository(Path.Combine(directory, "sessions")));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            Assert.NotNull(provider.GetRequiredService<IAppRunner>());
            Assert.NotNull(provider.GetRequiredService<IMainMenuService>());
            Assert.NotNull(provider.GetRequiredService<ISettingsMenuService>());
            Assert.NotNull(provider.GetRequiredService<IQuickConnectionMenuService>());
            Assert.NotNull(provider.GetRequiredService<IGroupSessionService>());
            Assert.NotNull(provider.GetRequiredService<IContainerMenuService>());
            Assert.NotNull(provider.GetRequiredService<IServerFormService>());
            Assert.NotNull(provider.GetRequiredService<IPathInputService>());
            Assert.NotNull(provider.GetRequiredService<ILocalCredentialService>());
            Assert.NotNull(provider.GetRequiredService<IActivityMenuService>());
            Assert.NotNull(provider.GetRequiredService<IMacroService>());
            Assert.NotNull(provider.GetRequiredService<IManagedCommandSessionService>());
            Assert.NotNull(provider.GetRequiredService<ITagMenuService>());
            Assert.NotNull(provider.GetRequiredService<IHostResolutionService>());
            Assert.NotNull(provider.GetRequiredService<IDnsResolver>());
            Assert.Equal(7, provider.GetServices<IMonitoringProvider>().Count());
            Assert.NotNull(provider.GetRequiredService<IMonitoringService>());
            Assert.NotNull(provider.GetRequiredService<IMonitoringMenuService>());
            Assert.NotNull(provider.GetRequiredService<IMacroEditorService>());
            Assert.NotNull(provider.GetRequiredService<IBatchMenuService>());
            Assert.NotNull(provider.GetRequiredService<ITargetViewMenuService>());
            Assert.NotNull(provider.GetRequiredService<IPortForwardingMenuService>());
            Assert.NotNull(provider.GetRequiredService<IConfigBackupMenuService>());
            Assert.NotNull(provider.GetRequiredService<IDiagnosticMenuService>());
            Assert.NotNull(provider.GetRequiredService<ICommandPaletteService>());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
