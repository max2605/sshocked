using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ContainerMenuService(IConfigRepository repository, IConsoleHelperService console,
    IContainerService containers, IContainerGroupService groups, IRecentConnectionService recents,
    IMenuFactory menu) : IContainerMenuService
{
    public async Task ShowScannedActions(ServerHost host, ContainerModel container)
    {
        var currentHost = repository.Load().Hosts.First(h => h.Id == host.Id);
        var isSaved = currentHost.SavedContainers.Any(saved => saved.ContainerId == container.ContainerId || saved.Name == container.Name);
        var entries = BuildSessionEntries(host, container);
        if (!isSaved)
        {
            entries.Add(new("Save...", 'V', executeAsync: () =>
            {
                containers.Save(host, container);
                return Task.CompletedTask;
            }));
        }
        entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
        await menu.RunMenu($"[bold yellow]{Markup.Escape(container.Name)}[/]", entries);
    }

    public async Task ShowSavedActions(ServerHost host, ContainerModel container, bool allowManagement = true)
    {
        var displayName = container.Alias ?? container.Name;
        var entries = BuildSessionEntries(host, container);
        entries.Insert(1, new("Toggle favorite", 'F', executeAsync: () =>
        {
            recents.ToggleFavorite(host, container);
            return Task.CompletedTask;
        }));
        if (allowManagement)
        {
            entries.Add(new("Edit", 'I', executeAsync: () =>
            {
                containers.Edit(host, container);
                return Task.CompletedTask;
            }));
            entries.Add(new("Delete", 'D', executeAsync: () =>
            {
                if (containers.Delete(host, container))
                    AnsiConsole.MarkupLine($"[green]✓[/] Container '[cyan]{Markup.Escape(displayName)}[/]' deleted.");
                else AnsiConsole.MarkupLine("[grey]Deletion cancelled.[/]");
                console.WaitForKey();
                return Task.CompletedTask;
            }));
        }
        entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
        await menu.RunMenu($"[bold yellow]{Markup.Escape(displayName)}[/]", entries);
    }

    public async Task SelectFromGroup(AppConfig config, List<ContainerModel> available)
    {
        if (available.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No containers available.[/]");
            console.WaitForKey();
            return;
        }
        var names = available.Select(container => container.Alias ?? container.Name).Append(MenuLabels.Back);
        var selectedName = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Select a [green]container[/] ([grey]clear search for Back[/]):")
            .PageSize(10).EnableSearch().UseConverter(Markup.Escape).AddChoices(names));
        if (selectedName == MenuLabels.Back) return;
        var container = available.First(container => (container.Alias ?? container.Name) == selectedName);
        var host = groups.FindParentServer(config, container);
        if (host is not null) await ShowSavedActions(host, container, false);
    }

    public void RenderGroup(string groupName, List<ContainerModel> available, AppConfig config)
    {
        var table = new Table().Border(TableBorder.Rounded)
            .Title($"[cyan]{Markup.Escape(groupName)}[/]")
            .AddColumns(new TableColumn("Alias"), new TableColumn("Container"), new TableColumn("Server"), new TableColumn("Image"));
        foreach (var container in available)
        {
            var server = groups.FindParentServer(config, container);
            table.AddRow(new Markup($"[bold]{Markup.Escape(container.Alias ?? container.Name)}[/]"),
                new Text(container.Name), new Text(server?.Alias ?? "?"), new Text(container.Image));
        }
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private List<MenuEntry> BuildSessionEntries(ServerHost host, ContainerModel container) =>
    [
        new("Exec (sh)", 'E', executeAsync: () => console.RunDockerExec(host, container)),
        new("Logs (-f)", 'L', executeAsync: () => console.RunDockerLogs(host, container))
    ];
}
