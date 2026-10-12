using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class CliDispatcherService(IConfigRepository configRepository, ISshRunnerService sshRunner, IProcessService processService, IConsoleHelperService consoleHelper, IEnumerable<IManagementCommandHandler> handlers, ILogger<CliDispatcherService> logger) : ICliDispatcherService
{

    public async Task<bool> Dispatch(ParseResult parseResult)
    {
        if (parseResult.TargetHostId is { } id)
        {
            var host = configRepository.Load().Hosts.FirstOrDefault(h => h.Id == id) ?? throw new ArgumentException("The selected host no longer exists.");
            if (parseResult.TargetContainerId is { } containerId)
            {
                var container = host.SavedContainers.FirstOrDefault(c => (c.Id ?? c.ContainerId) == containerId) ?? throw new ArgumentException("The selected container no longer exists.");
                await consoleHelper.RunDockerExec(host, container);
            }
            else await sshRunner.Connect(host);
            return true;
        }
        if (parseResult.ManagementCommand is { } command)
        {
            var handler = handlers.FirstOrDefault(h => h.Handles(command))
                ?? throw new ArgumentException($"Unknown command '{command}'.");
            await handler.Execute(command, parseResult.CommandArguments);
            return true;
        }
        if (parseResult.ShowHelp)
        {
            PrintHelp();
            return true;
        }

        if (parseResult.ShowVersion)
        {
            PrintVersion();
            return true;
        }

        if (parseResult.ShowList)
        {
            PrintList();
            return true;
        }

        if (parseResult.HasGroupCommand && parseResult.GroupName is not null && parseResult.Command is not null)
        {
            return await ExecuteGroupCommand(parseResult.GroupName, parseResult.Command);
        }

        if (parseResult.HasPositionalArg && parseResult.PositionalArg is not null)
        {
            return await ConnectToTarget(parseResult.PositionalArg);
        }

        return false;
    }

    private async Task<bool> ExecuteGroupCommand(string groupName, string command)
    {
        var config = configRepository.Load();

        var group = config.Groups.FirstOrDefault(g =>
            g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));

        if (group is null)
        {
            Environment.ExitCode = 1;
            Console.WriteLine($"Error: Group '{groupName}' not found.");
            Console.WriteLine("Run 'ssk --list' to see available groups.");
            return true;
        }

        var hosts = config.Hosts.Where(h => h.GroupId == group.Id).ToList();
        if (hosts.Count == 0)
        {
            Console.WriteLine($"Group '{group.Name}' has no servers.");
            return true;
        }

        Console.WriteLine($"Executing on group '{group.Name}' ({hosts.Count} server{(hosts.Count == 1 ? "" : "s")}):");
        Console.WriteLine($"  $ {command}");
        Console.WriteLine();

        var hasErrors = false;

        foreach (var host in hosts)
        {
            var header = $"-- {host.Alias} ({host.User}@{host.HostName}) ";
            Console.Write(header);
            Console.WriteLine(new string('-', Math.Max(1, 60 - header.Length)));

            try
            {
                var output = await processService.RunSshCommand(host, command);
                Console.Write(output);

                if (!output.EndsWith('\n'))
                {
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[error] {ex.Message}");
                hasErrors = true;
            }

            Console.WriteLine();
        }

        if (hasErrors)
        {
            Environment.ExitCode = 1;
            Console.WriteLine("Completed with errors on some servers.");
        }

        return true;
    }

    private async Task<bool> ConnectToTarget(string target)
    {
        var config = configRepository.Load();

        var host = config.Hosts.FirstOrDefault(h =>
            h.Alias.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (host is not null)
        {
            logger.LogInformation("Direct connect to server '{Alias}'", host.Alias);
            await sshRunner.Connect(host);
            return true;
        }

        host = config.Hosts.FirstOrDefault(h =>
            h.HostName.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (host is not null)
        {
            logger.LogInformation("Direct connect to hostname '{HostName}'", host.HostName);
            await sshRunner.Connect(host);
            return true;
        }

        var group = config.Groups.FirstOrDefault(g =>
            g.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (group is not null)
        {
            var groupHosts = config.Hosts.Where(h => h.GroupId == group.Id).ToList();
            if (groupHosts.Count == 0)
            {
                Console.WriteLine($"Group '{group.Name}' has no servers.");
                return true;
            }

            logger.LogInformation("Direct connect to group '{GroupName}' ({Count} hosts)", group.Name, groupHosts.Count);

            foreach (var groupHost in groupHosts)
            {
                Console.WriteLine($"Connecting to {groupHost.Alias} ({groupHost.HostName})...");
                await sshRunner.Connect(groupHost);
            }

            return true;
        }

        Console.WriteLine($"Error: No server or group found matching '{target}'.");
        Environment.ExitCode = 1;
        Console.WriteLine("Run 'ssk --help' for usage information.");
        return true;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("sshocked - Shell into servers and containers with lightning speed");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ssk                        Launch the interactive TUI");
        Console.WriteLine("  ssk <alias>                Connect directly to a server by alias");
        Console.WriteLine("  ssk <hostname>             Connect directly to a server by hostname");
        Console.WriteLine("  ssk <group>                Connect to all servers in a group");
        Console.WriteLine("  ssk --group, -g <name> -- <cmd>  Run a command on all servers in a group");
        Console.WriteLine("  ssk --list, -l             List all configured servers and groups");
        Console.WriteLine("  ssk --help, -h             Show this help message");
        Console.WriteLine("  ssk --version, -v          Show version information");
        Console.WriteLine("  ssk history [options]     Query audit events; --output json|jsonl|csv");
        Console.WriteLine("  ssk audit status|enable|disable|settings  Configure audit logging");
        Console.WriteLine("  ssk sessions list|replay|export|delete   Manage output recordings");
        Console.WriteLine("  ssk sessions status|enable|disable      Configure recording policy");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ssk prod-db-01             Connect to server 'prod-db-01'");
        Console.WriteLine("  ssk production             Connect to all servers in group 'production'");
        Console.WriteLine("  ssk -g production -- uptime     Run 'uptime' on all servers in 'production'");
        Console.WriteLine("  ssk -g web \"df -h /\"           Run 'df -h /' on all servers in 'web'");
    }

    private static void PrintVersion()
    {
        var version = typeof(CliDispatcherService).Assembly.GetName().Version;
        Console.WriteLine($"sshocked version {version?.ToString(3) ?? "0.0.0"}");
    }

    private void PrintList()
    {
        var config = configRepository.Load();

        if (config.Groups.Count == 0 && config.Hosts.Count == 0)
        {
            Console.WriteLine("No servers configured.");
            return;
        }

        if (config.Groups.Count > 0)
        {
            Console.WriteLine("Groups:");
            foreach (var group in config.Groups)
            {
                var count = config.Hosts.Count(h => h.GroupId == group.Id);
                Console.WriteLine($"  {group.Name} ({count} server{(count == 1 ? "" : "s")})");
            }
            Console.WriteLine();
        }

        if (config.Hosts.Count > 0)
        {
            Console.WriteLine("Servers:");
            foreach (var host in config.Hosts)
            {
                var groupName = config.Groups
                    .FirstOrDefault(g => g.Id == host.GroupId)?.Name ?? "(uncategorized)";
                Console.WriteLine($"  {host.Alias,-20} {host.User}@{host.HostName}:{host.Port}  [{groupName}]");
            }
        }
    }
}


