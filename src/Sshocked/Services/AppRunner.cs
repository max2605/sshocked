using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class AppRunner(IConfigRepository configRepository, ISshConfigImporter sshConfigImporter, ISshConfigSyncService sync, IMainMenuService mainMenu, IConsoleWriterService console, ILogger<AppRunner> logger) : IAppRunner
{
    public async Task Run()
    {
        logger.LogInformation("AppRunner starting");

        var config = configRepository.Load();

        if (config.IsFirstStart)
        {
            console.WriteLine("Welcome to sshocked! Your configuration has been initialized.");
            logger.LogInformation("First start detected");

            var sshConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ssh", "config");

            if (File.Exists(sshConfigPath))
            {
                console.Write($"Found SSH config at {sshConfigPath}. Import hosts? (y/N): ");
                var response = console.ReadLine()?.Trim().ToLowerInvariant();

                if (response is "y" or "yes")
                {
                    var importedHosts = sshConfigImporter.Import();
                    config.Hosts.AddRange(importedHosts);
                    console.WriteLine($"Imported {importedHosts.Count} host(s) from your SSH config.");
                    logger.LogInformation("Imported {Count} host(s) from SSH config", importedHosts.Count);
                }
                else
                {
                    console.WriteLine("Skipping SSH config import.");
                    logger.LogInformation("User skipped SSH config import");
                }
            }

            config.IsFirstStart = false;
            configRepository.Save(config);
        }

        sync.Initialize();
        await mainMenu.Show();

        logger.LogInformation("AppRunner finished");
    }
}
