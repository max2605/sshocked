using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class PortForwardingMenuService(IPortForwardingService forwarding, IConfigRepository repository,
    IMenuFactory menus, IConsoleWriterService console) : IPortForwardingMenuService
{
    public async Task Show()
    {
        while (true)
        {
            console.WriteLine(ForwardingDisplay.Format(forwarding.Status(), "table"));
            var entries = forwarding.List().Select(profile => new MenuEntry(Markup.Escape(profile.Name) + " — " + Markup.Escape(ForwardingValidation.Route(profile)),
                executeAsync: () => Actions(profile.Id))).ToList();
            entries.Add(new("Create forwarding profile", 'A', executeAsync: () => Edit(null)));
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menus.RunMenu("SSH forwarding profiles", entries)) return;
        }
    }

    private async Task Actions(string id)
    {
        while (true)
        {
            console.WriteLine(ForwardingDisplay.Format(forwarding.Status().Where(status => status.ProfileId == id).ToList(), "table"));
            var entries = new List<MenuEntry>
            {
                new("Start forwarding", 'S', executeAsync: () => Start(id)),
                new("Stop forwarding", 'O', executeAsync: () => forwarding.Stop(id)),
                new("Edit profile", 'E', executeAsync: () => Edit(forwarding.Find(id))),
                new("Delete profile", 'D', executeAsync: () => Delete(id)),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            if (!await menus.RunMenu("Forwarding profile actions", entries)) return;
            if (forwarding.List().All(profile => profile.Id != id)) return;
        }
    }

    private Task Edit(ForwardingProfile? original)
    {
        var profile = original is null ? new ForwardingProfile() : System.Text.Json.JsonSerializer.Deserialize(
            System.Text.Json.JsonSerializer.Serialize(original, ForwardingJsonContext.Default.ForwardingProfile), ForwardingJsonContext.Default.ForwardingProfile)!;
        var name = Prompt("Profile name (blank cancels)", null);
        if (string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        profile.Name = name;
        foreach (var host in repository.Load().Hosts) console.WriteLine($"{host.Id}: {BatchReportFormatter.Safe(host.Alias)}");
        var server = Prompt("Server ID or alias", original?.HostId);
        if (server is null) return Task.CompletedTask;
        var hosts = repository.Load().Hosts.Where(host => host.Id == server || host.Alias.Equals(server, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hosts.Count != 1) { console.WriteLine("Server not found or ambiguous."); return Task.CompletedTask; }
        profile.HostId = hosts[0].Id;
        var kind = Prompt("Kind: local, remote, dynamic", profile.Kind.ToString());
        if (kind is null) return Task.CompletedTask;
        if (!Enum.TryParse<ForwardingKind>(kind, true, out var parsed)) { console.WriteLine("Unknown forwarding kind."); return Task.CompletedTask; }
        profile.Kind = parsed;
        var bind = Prompt("Listener bind address", profile.BindAddress);
        var port = Prompt("Listener port", original?.ListenPort.ToString());
        if (bind is null || port is null) return Task.CompletedTask;
        profile.BindAddress = bind;
        if (!int.TryParse(port, out var listenPort)) { console.WriteLine("Invalid listener port."); return Task.CompletedTask; }
        profile.ListenPort = listenPort;
        if (!System.Net.IPAddress.IsLoopback(ForwardingValidation.BindAddress(bind)))
        {
            console.Write("Explicitly allow this non-loopback listener? (yes/N): ");
            profile.AllowNonLoopbackBind = Confirmed();
        }
        else profile.AllowNonLoopbackBind = false;
        if (profile.Kind != ForwardingKind.Dynamic)
        {
            var target = Prompt("Destination host", profile.TargetHost);
            var targetPort = Prompt("Destination port", original?.TargetPort.ToString());
            if (target is null || targetPort is null) return Task.CompletedTask;
            profile.TargetHost = target;
            if (!int.TryParse(targetPort, out var destinationPort)) { console.WriteLine("Invalid destination port."); return Task.CompletedTask; }
            profile.TargetPort = destinationPort;
        }
        try { forwarding.Save(profile, original); console.WriteLine("Saved: " + ForwardingValidation.Route(profile)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { console.WriteLine(exception.Message); }
        return Task.CompletedTask;
    }

    private async Task Start(string id)
    {
        var profile = forwarding.Find(id);
        console.WriteLine(ForwardingValidation.Route(profile));
        console.Write("Start this forwarding profile? (yes/N): ");
        if (!Confirmed()) return;
        try { console.WriteLine(ForwardingDisplay.Format([await forwarding.Start(id)], "table")); }
        catch (Exception exception) { console.WriteLine("Forwarding failed: " + exception.GetType().Name + "; " + (AuditRedactor.Redact(exception.Message) ?? "unavailable")); }
    }

    private Task Delete(string id)
    {
        console.Write("Delete this stopped forwarding profile? (yes/N): ");
        if (Confirmed()) forwarding.Delete(id);
        return Task.CompletedTask;
    }

    private string? Prompt(string label, string? fallback)
    {
        console.Write(label + (fallback is null ? ": " : $" [{fallback}]: "));
        var value = console.ReadLine();
        return value is null ? null : string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private bool Confirmed() => console.ReadLine()?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
}
