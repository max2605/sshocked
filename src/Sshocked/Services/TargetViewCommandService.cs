using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class TargetViewCommandService(ITargetViewService views, IConsoleWriterService console,
    IGroupConnectionService connections, BatchCommandService batches) : IManagementCommandHandler
{
    public bool Handles(string command) => command.Equals("views", StringComparison.OrdinalIgnoreCase);

    public async Task Execute(string command, string[] arguments)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        if (action is "command" or "macro" or "upload" or "download")
        {
            if (arguments.Length < 2) throw new ArgumentException("Select a target view name or ID.");
            await batches.Execute("batch", [action, "--view", arguments[1], .. arguments[2..]]);
            return;
        }
        var options = new CommandOptions(arguments.Length == 0 ? [] : arguments[1..], ["expression", "name"], ["force", "execute"]);
        switch (action)
        {
            case "list":
                Require(options, 0);
                foreach (var view in views.List()) console.WriteLine($"{view.Id} {view.Name}: {view.Expression} ({views.Resolve(view.Id).Count} targets)");
                break;
            case "create":
                Require(options, 1, "expression");
                console.WriteLine("Created target view: " + views.Save(options.Positionals[0], options.Get("expression") ?? throw new ArgumentException("Use --expression EXPR.")).Name);
                break;
            case "edit":
                Require(options, 1, "expression", "name");
                if (!options.Has("expression") && !options.Has("name")) throw new ArgumentException("Supply --name or --expression to edit.");
                var current = views.Find(options.Positionals[0]);
                views.Save(options.Get("name", current.Name)!, options.Get("expression", current.Expression)!, current.Id);
                console.WriteLine("Target view updated.");
                break;
            case "delete":
                Require(options, 1, "force");
                if (!options.Has("force")) throw new ArgumentException("Use views delete NAME --force to confirm deletion.");
                views.Delete(options.Positionals[0]);
                console.WriteLine("Target view deleted.");
                break;
            case "preview":
            case "select":
            case "connect":
                Require(options, 1, action == "connect" ? "execute" : "unused");
                var definition = views.Find(options.Positionals[0]);
                var hosts = views.Resolve(definition.Id);
                console.WriteLine($"{definition.Name}: {definition.Expression}; {hosts.Count} target(s).");
                foreach (var host in hosts) console.WriteLine($"  {BatchReportFormatter.Safe(host.Alias)} ({host.Id})");
                if (hosts.Count == 0) console.WriteLine("The target selection is empty. No operation was started.");
                else if (action == "connect" && options.Has("execute"))
                    await connections.ConnectAllMultiTab(hosts.Select(host => new Models.BatchTarget(host).CreateHost()).ToList());
                else if (action == "connect") console.WriteLine("Preview only. Repeat with --execute to connect.");
                break;
            default: throw new ArgumentException("Use views list|create|edit|delete|preview|select|connect|command|macro|upload|download.");
        }
    }

    private static void Require(CommandOptions options, int count, params string[] allowed)
    {
        if (options.Positionals.Count != count || new[] { "expression", "name", "force", "execute" }.Any(name => options.Has(name) && !allowed.Contains(name)))
            throw new ArgumentException("Unexpected arguments for this target-view action.");
    }
}
