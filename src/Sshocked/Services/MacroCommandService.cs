using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MacroCommandService(IMacroService macros, IManagedCommandSessionService commandSession,
    IConfigRepository repository, IConsoleWriterService console) : IManagementCommandHandler
{
    public bool Handles(string command) => command.Equals("macros", StringComparison.OrdinalIgnoreCase);

    public async Task Execute(string command, string[] arguments)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        var options = new CommandOptions(arguments.Length == 0 ? [] : arguments[1..], ["server", "group", "tag"], ["force"]);
        switch (action)
        {
            case "list":
                RequireNoTargetOptions(options);
                if (options.Positionals.Count > 0) throw new ArgumentException("Use macros list.");
                foreach (var macro in macros.List()) console.WriteLine($"{macro.Id} {macro.Name} ({macro.Commands.Count} commands)");
                break;
            case "record":
                if (options.Positionals.Count != 0 || options.Has("force")) throw new ArgumentException("Use macros record --server ALIAS.");
                var recordingHosts = Targets(options);
                if (!options.Has("server") || recordingHosts.Count != 1) throw new ArgumentException("Use macros record --server ALIAS.");
                await commandSession.Show(recordingHosts[0]);
                break;
            case "run":
                if (options.Positionals.Count != 1 || options.Has("force")) throw new ArgumentException("Use macros run NAME --server ALIAS|--group NAME|--tag TAG.");
                await Run(macros.Find(options.Positionals[0]), Targets(options));
                break;
            case "delete":
                RequireNoTargetOptions(options);
                if (options.Positionals.Count != 1 || !options.Has("force")) throw new ArgumentException("Use macros delete NAME --force to confirm deletion.");
                macros.Delete(macros.Find(options.Positionals[0]).Id); console.WriteLine("Macro deleted.");
                break;
            default: throw new ArgumentException("Use macros list|record --server ALIAS|run NAME --server ALIAS|--group NAME|--tag TAG|delete NAME --force.");
        }
    }

    private List<ServerHost> Targets(CommandOptions options)
    {
        if (new[] { "server", "group", "tag" }.Count(options.Has) != 1) throw new ArgumentException("Select exactly one --server, --group or --tag target.");
        var config = repository.Load();
        if (options.Get("server") is { } server)
        {
            var hosts = config.Hosts.Where(host => host.Id.Equals(server, StringComparison.OrdinalIgnoreCase) || host.Alias.Equals(server, StringComparison.OrdinalIgnoreCase)).ToList();
            return hosts.Count == 1 ? hosts : throw new ArgumentException("Server not found or ambiguous; use its ID.");
        }
        if (options.Get("group") is { } name)
        {
            var groups = config.Groups.Where(group => group.Id.Equals(name, StringComparison.OrdinalIgnoreCase) || group.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (groups.Count != 1) throw new ArgumentException("Group not found or ambiguous; use its ID.");
            return config.Hosts.Where(host => host.GroupId == groups[0].Id).ToList();
        }
        return config.Hosts.Where(host => host.Tags.Contains(options.Get("tag")!, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private async Task Run(MacroDefinition macro, IReadOnlyList<ServerHost> hosts)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var results = await macros.Run(macro, hosts, console.Write, cancellation.Token);
            if (results.Any(result => !result.Success)) Environment.ExitCode = 1;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { console.WriteLine("Macro execution cancelled."); Environment.ExitCode = 130; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static void RequireNoTargetOptions(CommandOptions options)
    {
        if (options.Has("server") || options.Has("group") || options.Has("tag")) throw new ArgumentException("This macro action does not accept target options.");
    }
}
