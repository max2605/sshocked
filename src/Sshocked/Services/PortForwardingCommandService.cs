using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class PortForwardingCommandService(IPortForwardingService forwarding,
    IConfigRepository repository, IConsoleWriterService console) : IManagementCommandHandler
{
    public bool Handles(string command) => command.Equals("forward", StringComparison.OrdinalIgnoreCase);

    public async Task Execute(string command, string[] arguments)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "status";
        var options = new CommandOptions(arguments.Length == 0 ? [] : arguments[1..],
            ["server", "kind", "bind", "listen-port", "target", "target-port", "timeout", "name", "output"],
            ["allow-external", "loopback-only", "force"]);
        switch (action)
        {
            case "list":
            case "status":
                Require(options, 0, "output");
                console.WriteLine(ForwardingDisplay.Format(forwarding.Status(), options.Get("output", "table")!));
                break;
            case "create":
                Require(options, 1, "server", "kind", "bind", "listen-port", "target", "target-port", "timeout", "allow-external", "loopback-only");
                var profile = Build(new() { Name = options.Positionals[0] }, options);
                forwarding.Save(profile);
                console.WriteLine("Created: " + profile.Name + "; " + ForwardingValidation.Route(profile));
                break;
            case "edit":
                Require(options, 1, "server", "kind", "bind", "listen-port", "target", "target-port", "timeout", "allow-external", "loopback-only", "name");
                var original = forwarding.Find(options.Positionals[0]);
                var edited = System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(original, ForwardingJsonContext.Default.ForwardingProfile), ForwardingJsonContext.Default.ForwardingProfile)!;
                forwarding.Save(Build(edited, options), original);
                console.WriteLine("Forwarding profile updated.");
                break;
            case "delete":
                Require(options, 1, "force");
                if (!options.Has("force")) throw new ArgumentException("Use forward delete NAME --force.");
                forwarding.Delete(options.Positionals[0]);
                console.WriteLine("Forwarding profile deleted.");
                break;
            case "start":
                Require(options, 1);
                await Start(options.Positionals[0]);
                break;
            case "stop":
                Require(options, 1);
                await forwarding.Stop(options.Positionals[0]);
                console.WriteLine("Forwarding profile stopped.");
                break;
            default: throw new ArgumentException("Use forward create|edit|delete|list|status|start|stop.");
        }
    }

    private ForwardingProfile Build(ForwardingProfile profile, CommandOptions options)
    {
        if (options.Get("server") is { } server)
        {
            var hosts = repository.Load().Hosts.Where(host => host.Id.Equals(server, StringComparison.OrdinalIgnoreCase) || host.Alias.Equals(server, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hosts.Count != 1) throw new ArgumentException("Server not found or ambiguous; use its ID.");
            profile.HostId = hosts[0].Id;
        }
        profile.Name = options.Get("name", profile.Name)!;
        if (options.Get("kind") is { } kind) profile.Kind = kind.ToLowerInvariant() switch
        { "local" => ForwardingKind.Local, "remote" => ForwardingKind.Remote, "dynamic" => ForwardingKind.Dynamic, _ => throw new ArgumentException("Use --kind local|remote|dynamic.") };
        profile.BindAddress = options.Get("bind", profile.BindAddress)!;
        profile.ListenPort = options.Integer("listen-port", profile.ListenPort, 1, 65535);
        profile.TargetHost = options.Get("target", profile.TargetHost)!;
        profile.TargetPort = options.Integer("target-port", profile.TargetPort, 1, 65535);
        profile.StartupTimeoutSeconds = options.Integer("timeout", profile.StartupTimeoutSeconds, 1, 120);
        if (options.Has("allow-external") && options.Has("loopback-only")) throw new ArgumentException("Choose --allow-external or --loopback-only.");
        if (options.Has("allow-external")) profile.AllowNonLoopbackBind = true;
        if (options.Has("loopback-only")) profile.AllowNonLoopbackBind = false;
        return profile;
    }

    private async Task Start(string nameOrId)
    {
        var profile = forwarding.Find(nameOrId);
        console.WriteLine("Starting: " + BatchReportFormatter.Safe(ForwardingValidation.Route(profile)));
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var status = await forwarding.Start(profile.Id, cancellation.Token);
            console.WriteLine(ForwardingDisplay.Format([status], "table"));
            console.WriteLine("Forwarding remains owned by this foreground process. Ctrl+C stops it; another terminal can use forward status|stop.");
            await forwarding.Wait(profile.Id, cancellation.Token);
            var ended = forwarding.Status().First(item => item.ProfileId == profile.Id);
            if (ended.State == ForwardingState.Failed) { console.WriteLine(ForwardingDisplay.Format([ended], "table")); Environment.ExitCode = 1; }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { console.WriteLine("Forwarding cancelled."); Environment.ExitCode = 130; }
        finally
        {
            Console.CancelKeyPress -= cancel;
            await forwarding.Stop(profile.Id);
        }
    }

    private static void Require(CommandOptions options, int count, params string[] allowed)
    {
        if (options.Positionals.Count != count || new[] { "server", "kind", "bind", "listen-port", "target", "target-port", "timeout", "name", "output", "allow-external", "loopback-only", "force" }
            .Any(option => options.Has(option) && !allowed.Contains(option))) throw new ArgumentException("Unexpected arguments for this forwarding action.");
    }
}
