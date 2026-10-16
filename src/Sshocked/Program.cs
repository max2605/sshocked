using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        // Establish Unicode before Spectre detects capabilities or captures Console.Out.
        // Legacy Windows code pages replace badge characters and change their displayed width.
        Console.OutputEncoding = new UTF8Encoding(false);

        // OpenSSH invokes this executable as an askpass helper with a prompt argument.
        // Resolve it before configuration, DI and logging so only the requested password reaches stdout.
        if (Environment.GetEnvironmentVariable(SshAskPass.HelperEnvironment) == "1")
        {
            Console.WriteLine(Environment.GetEnvironmentVariable(SshAskPass.PasswordEnvironment) ?? "");
            return;
        }

        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalConsoleOutput()
        });

        var services = new ServiceCollection();
        ServiceRegistration.Configure(services);
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
        catch (AzureCliCertificateException ex)
        {
            var message = AuditRedactor.Redact(ex.Message);
            logger.LogWarning("{Message}", message);
            Console.Error.WriteLine($"Error: {message}");
            Environment.ExitCode = args.FirstOrDefault()?.Equals("monitor", StringComparison.OrdinalIgnoreCase) == true ? 2 : 1;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Application terminated with unhandled exception");
            Console.Error.WriteLine($"Error: {AuditRedactor.Redact(ex.Message)}");
            Environment.ExitCode = ex is OperationCanceledException ? 130
                : args.FirstOrDefault()?.Equals("monitor", StringComparison.OrdinalIgnoreCase) == true ? 2 : 1;
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
    }
}
