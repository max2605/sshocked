using Spectre.Console;
using Spectre.Console.Rendering;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class TableRendererService(IHostReachabilityService reachability, IConfigRepository repository) : ITableRendererService
{
    public void RenderServerTable(AppConfig config)
    {
        if (config.Hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No hosts configured yet.[/]");
            AnsiConsole.WriteLine();
            return;
        }
        if (config.UI.ShowReachabilityStatus)
        {
            var palette = UiThemePalette.For(config.UI.Theme);
            var checking = config.UI.EnableReachabilityChecks ? "Background checks enabled" : "Background checks disabled";
            AnsiConsole.MarkupLine($"[{palette.Muted}]TCP status: online = port accepts a connection; offline = refused/timed out. {checking}. Routes via jump/Bastion are not probed directly.[/]");
        }
        var uncategorized = config.Hosts.Where(h => string.IsNullOrEmpty(h.GroupId) || !config.Groups.Any(g => g.Id == h.GroupId)).ToList();
        if (uncategorized.Count > 0) AnsiConsole.Write(BuildTable("Uncategorized", uncategorized, config));
        foreach (var group in config.Groups)
        {
            var hosts = config.Hosts.Where(h => h.GroupId == group.Id).ToList();
            if (hosts.Count > 0) AnsiConsole.Write(BuildTable(group.Name, hosts, config));
        }
        AnsiConsole.WriteLine();
    }

    public void RenderGroup(string groupName, List<ServerHost> hosts)
    {
        AnsiConsole.Write(BuildTable(groupName, hosts, repository.Load()));
        AnsiConsole.WriteLine();
    }

    public Table BuildTable(string groupName, List<ServerHost> hosts, AppConfig config)
    {
        var ui = config.UI;
        var palette = UiThemePalette.For(ui.Theme);
        var color = palette.Accent;
        var table = new Table().Border(ui.CompactMode ? TableBorder.Simple : TableBorder.Rounded)
            .Title($"[{color}]{Markup.Escape(groupName)}[/]");
        table.BorderStyle = Style.Parse($"{color} on {palette.Background}");
        if (!ui.CompactMode) table.Expand();
        if (ui.ShowReachabilityStatus) table.AddColumn(new TableColumn($"[{color} bold]TCP status[/]"));
        table.AddColumns(new[] { "Alias", "Host", "User", "Port", "Auth", "Docker", "Tags" }
            .Select(label => new TableColumn($"[{color} bold]{label}[/]")).ToArray());
        if (ui.ShowLastConnectedColumn) table.AddColumn(new TableColumn($"[{color} bold]Last connected[/]"));
        if (ui.CompactMode)
            foreach (var column in table.Columns) column.Padding = new Padding(0, 0, 1, 0);
        var now = DateTimeOffset.UtcNow;
        foreach (var host in hosts.OrderByDescending(h => h.IsFavorite).ThenBy(h => h.Alias, StringComparer.OrdinalIgnoreCase))
        {
            var row = new List<IRenderable>();
            if (ui.ShowReachabilityStatus) row.Add(new Markup(HostVisuals.ReachabilityBadge(reachability.GetStatus(host, ui.EnableReachabilityChecks), ui.Theme)));
            row.AddRange([new Markup(HostVisuals.Alias(host, ui.Theme)), new Text(host.HostName), new Text(host.User),
                new Text(host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)), new Markup(HostVisuals.AuthBadge(host, ui.Theme)),
                new Markup(HostVisuals.DockerBadge(host, ui.Theme)), new Text(string.Join(", ", host.Tags))]);
            if (ui.ShowLastConnectedColumn)
            {
                var last = config.RecentConnections.Where(r => r.HostId == host.Id && r.ContainerId is null)
                    .Select(r => (DateTimeOffset?)r.LastConnectedAt).Max();
                row.Add(last is null ? new Markup($"[{palette.Muted}]Never[/]") : new Text(HostVisuals.RelativeTime(last, now)));
            }
            table.AddRow(row);
            if (ui.CompactMode) continue;
            foreach (var container in host.SavedContainers.OrderByDescending(c => c.IsFavorite))
            {
                var cells = new List<IRenderable>();
                if (ui.ShowReachabilityStatus) cells.Add(new Text(""));
                cells.Add(new Markup($"  {(container.IsFavorite ? $"[{palette.Warning}]★[/] " : "")}[{palette.Muted}]{Markup.Escape(container.Alias ?? container.Name)}[/]"));
                cells.Add(new Text(container.Image));
                while (cells.Count < table.Columns.Count) cells.Add(new Text(""));
                if (ui.ShowLastConnectedColumn)
                {
                    var id = container.Id ?? container.ContainerId;
                    var last = config.RecentConnections.Where(r => r.HostId == host.Id && r.ContainerId == id)
                        .Select(r => (DateTimeOffset?)r.LastConnectedAt).Max();
                    cells[^1] = last is null ? new Markup($"[{palette.Muted}]Never[/]") : new Text(HostVisuals.RelativeTime(last, now));
                }
                table.AddRow(cells);
            }
        }
        return table;
    }

    public string GetAuthLabel(AuthType authType) => authType switch
    {
        AuthType.Password => "Password",
        AuthType.SshAgent => "Agent",
        AuthType.CustomConfig => "Custom",
        AuthType.AzureBastion => "Azure Bastion",
        _ => "SSH Key"
    };
}
