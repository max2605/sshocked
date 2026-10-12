using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;
using System.ComponentModel;

namespace Sshocked.Services;

public sealed class TableRendererService : ITableRendererService
{
    public void RenderServerTable(AppConfig config)
    {
        var groups = config.Groups;
        var hosts = config.Hosts;

        if (hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No hosts configured yet.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var uncategorized = hosts.Where(h =>
            string.IsNullOrEmpty(h.GroupId) ||
            !groups.Any(g => g.Id == h.GroupId)).ToList();

        if (uncategorized.Count > 0)
        {
            RenderGroup("Uncategorized", uncategorized);
        }

        foreach (var group in groups)
        {
            var groupHosts = hosts.Where(h => h.GroupId == group.Id).ToList();
            if (groupHosts.Count > 0)
            {
                RenderGroup(group.Name, groupHosts);
            }
        }

        AnsiConsole.WriteLine();
    }

    public void RenderGroup(string groupName, List<ServerHost> hosts)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[cyan]{groupName}[/]")
            .Expand()
            .AddColumns([
                new TableColumn("Alias"),
            new TableColumn("Host"),
            new TableColumn("User"),
            new TableColumn("Port"),
            new TableColumn("Auth")
            ]);

        for (int i = 0; i < hosts.Count; i++)
        {
            var host = hosts[i];

            // 1. Add Host row
            table.AddRow(
                new Markup($"{(host.IsFavorite ? "[yellow]★[/] " : "")}[bold]{Markup.Escape(host.Alias)}[/]"),
                new Text(host.HostName),
                new Text(host.User),
                new Text(host.Port.ToString()),
                new Text(GetAuthLabel(host.AuthType)));

            // 2. Add Container rows
            foreach (var container in host.SavedContainers)
            {
                table.AddRow(
                    new Markup($"{(container.IsFavorite ? "[yellow]★[/] " : "")}[grey]{Markup.Escape(container.Name)}[/]"),
                    new Markup($"[grey]{Markup.Escape(container.Image)}[/]"),
                    new Text(""),
                    new Text(""),
                    new Text(""));
            }

            if (i < hosts.Count - 1)
            {
                table.AddRow(
                    new Rule().RuleStyle("grey35"),
                    new Rule().RuleStyle("grey35"),
                    new Rule().RuleStyle("grey35"),
                    new Rule().RuleStyle("grey35"),
                    new Rule().RuleStyle("grey35"));
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public string GetAuthLabel(AuthType authType) => authType switch
    {
        AuthType.Password => "Password",
        AuthType.SshAgent => "Agent",
        AuthType.CustomConfig => "Custom",
        _ => "SSH Key"
    };
}
