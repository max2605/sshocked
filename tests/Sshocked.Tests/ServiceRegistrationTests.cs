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
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
