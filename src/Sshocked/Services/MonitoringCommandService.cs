using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MonitoringCommandService(IMonitoringService monitoring, IConfigRepository repository,
    IConsoleWriterService console) : IManagementCommandHandler
{
    public bool Handles(string command) => command.Equals("monitor", StringComparison.OrdinalIgnoreCase);

    public async Task Execute(string command, string[] arguments)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "status";
        var options = new CommandOptions(arguments.Length == 0 ? [] : arguments[1..],
            ["server", "group", "tag", "kind", "output", "file", "alarm"]);
        if (action is "enable" or "disable")
        {
            RequireNoTargets(options);
            RejectUnexpected(options);
            if (options.Positionals.Count == 0) repository.Update(config => config.Monitoring.Enabled = action == "enable");
            else if (options.Positionals.Count == 1)
            {
                var original = monitoring.Find(options.Positionals[0]);
                var copy = Clone(original);
                copy.Enabled = action == "enable";
                monitoring.Save(copy, original);
            }
            else throw new ArgumentException("Use monitor enable|disable [NAME].");
            console.WriteLine("Monitoring setting saved."); return;
        }
        if (action == "create")
        {
            RequireNoTargets(options);
            RejectUnexpected(options, "file");
            if (options.Positionals.Count > 0) throw new ArgumentException("Use monitor create --file definition.json.");
            var path = options.Get("file") ?? throw new ArgumentException("Use monitor create --file definition.json.");
            var info = new FileInfo(path);
            if (info.Length > 65536) throw new ArgumentException("Monitor definition exceeds 64 KiB.");
            var definition = JsonSerializer.Deserialize(File.ReadAllText(path), MonitoringJsonContext.Default.MonitorDefinition)
                ?? throw new ArgumentException("The file contains no monitor definition.");
            monitoring.Save(definition);
            console.WriteLine("Created monitor " + definition.Id); return;
        }
        if (action == "delete")
        {
            RequireNoTargets(options);
            RejectUnexpected(options);
            if (options.Positionals.Count != 1) throw new ArgumentException("Use monitor delete NAME.");
            await monitoring.Delete(monitoring.Find(options.Positionals[0]).Id); return;
        }
        if (action == "ack")
        {
            RequireNoTargets(options);
            RejectUnexpected(options, "alarm");
            if (options.Positionals.Count != 1) throw new ArgumentException("Use monitor ack NAME --alarm KEY.");
            await monitoring.Acknowledge(monitoring.Find(options.Positionals[0]).Id,
                options.Get("alarm") ?? throw new ArgumentException("An alarm key is required."));
            return;
        }
        if (options.Positionals.Count > 0 || options.Has("file") || options.Has("alarm"))
            throw new ArgumentException("Unexpected monitoring argument.");
        var hosts = Resolve(options);
        var kind = options.Get("kind");
        if (kind is not null && !monitoring.Providers.Any(provider => provider.Kind == kind))
            throw new ArgumentException("Unknown monitoring kind.");
        var format = options.Get("output", "table");
        if (format is not ("table" or "json")) throw new ArgumentException("Output must be table or json.");
        void Publish(IReadOnlyList<MonitorSnapshot> snapshots)
        {
            if (format == "json") console.WriteLine(JsonSerializer.Serialize(snapshots.ToList(), MonitoringJsonContext.Default.ListMonitorSnapshot));
            else MonitoringDisplay.Write(console, snapshots);
            Environment.ExitCode = MonitoringDisplay.ExitCode(snapshots);
        }
        if (action == "status") { Publish(monitoring.Status(hosts, kind)); return; }
        if (action is not ("run" or "watch")) throw new ArgumentException("Use monitor status|run|watch|create|delete|enable|disable|ack.");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (action == "run") Publish(await monitoring.Run(hosts, kind, cancellation.Token));
            else await monitoring.Watch(Publish, hosts, kind, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Environment.ExitCode = 130; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private IReadOnlyList<string>? Resolve(CommandOptions options)
    {
        var count = new[] { "server", "group", "tag" }.Count(options.Has);
        if (count == 0) return null;
        if (count != 1) throw new ArgumentException("Select one --server, --group or --tag.");
        var config = repository.Load();
        if (options.Get("server") is { } server)
        {
            var matches = config.Hosts.Where(host => host.Id == server || host.Alias.Equals(server, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? [matches[0].Id] : throw new ArgumentException("Host not found or ambiguous.");
        }
        if (options.Get("group") is { } name)
        {
            var groups = config.Groups.Where(group => group.Id == name || group.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (groups.Count != 1) throw new ArgumentException("Group not found or ambiguous.");
            return config.Hosts.Where(host => host.GroupId == groups[0].Id).Select(host => host.Id).ToList();
        }
        return config.Hosts.Where(host => host.Tags.Contains(options.Get("tag")!, StringComparer.OrdinalIgnoreCase)).Select(host => host.Id).ToList();
    }

    private static void RequireNoTargets(CommandOptions options)
    {
        if (new[] { "server", "group", "tag", "kind", "output" }.Any(options.Has))
            throw new ArgumentException("This action does not accept a target filter.");
    }
    private static void RejectUnexpected(CommandOptions options, string? allowed = null)
    {
        if (new[] { "file", "alarm" }.Any(key => key != allowed && options.Has(key)))
            throw new ArgumentException("Unexpected monitoring option.");
    }
    private static MonitorDefinition Clone(MonitorDefinition value) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(value, MonitoringJsonContext.Default.MonitorDefinition), MonitoringJsonContext.Default.MonitorDefinition)!;
}
