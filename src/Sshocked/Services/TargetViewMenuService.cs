using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class TargetViewMenuService(ITargetViewService views, IMenuFactory menus,
    IConsoleWriterService console, IBatchMenuService batches, IGroupConnectionService connections) : ITargetViewMenuService
{
    public async Task Show()
    {
        while (true)
        {
            var entries = views.List().Select(view => new MenuEntry(Markup.Escape(view.Name) + " — " + Markup.Escape(view.Expression),
                executeAsync: () => ShowView(view.Id))).ToList();
            entries.Add(new("Create target view", 'A', executeAsync: () => Edit(null)));
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menus.RunMenu("Named target views", entries)) return;
        }
    }

    private async Task ShowView(string id)
    {
        while (true)
        {
            var view = views.Find(id);
            var hosts = Preview(view);
            var entries = new List<MenuEntry>
            {
                new("Edit view", 'E', executeAsync: () => Edit(view)),
                new("Delete view", 'D', executeAsync: () => Delete(id))
            };
            if (hosts.Count > 0)
            {
                entries.Add(new("Connect matching servers", 'C', executeAsync: () => Connect(id)));
                entries.Add(new("Run command", 'R', executeAsync: () => batches.RunCommand(() => views.Resolve(id))));
                entries.Add(new("Run macro", 'M', executeAsync: () => batches.RunMacro(() => views.Resolve(id))));
                entries.Add(new("Upload to matching servers", 'U', executeAsync: () => batches.Upload(() => views.Resolve(id))));
                entries.Add(new("Download from matching servers", 'O', executeAsync: () => batches.Download(() => views.Resolve(id))));
            }
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menus.RunMenu("Target view actions", entries)) return;
            if (views.List().All(existing => existing.Id != id)) return;
        }
    }

    private Task Edit(TargetViewDefinition? current)
    {
        console.Write($"View name (blank {(current is null ? "cancels" : "keeps " + current.Name)}): ");
        var name = console.ReadLine();
        if (string.IsNullOrWhiteSpace(name))
        {
            if (current is null) return Task.CompletedTask;
            name = current.Name;
        }
        console.WriteLine("Expression: AND, OR, NOT and parentheses; quote tags containing spaces or operator names.");
        console.Write("Tag expression (blank keeps current or cancels): ");
        var expression = console.ReadLine();
        if (string.IsNullOrWhiteSpace(expression)) expression = current?.Expression;
        if (string.IsNullOrWhiteSpace(expression)) return Task.CompletedTask;
        try
        {
            var view = views.Save(name, expression, current?.Id);
            Preview(view);
        }
        catch (ArgumentException error) { console.WriteLine(error.Message); }
        return Task.CompletedTask;
    }

    private Task Delete(string id)
    {
        console.Write("Delete this named view? (yes/N): ");
        if (Confirmed()) views.Delete(id);
        return Task.CompletedTask;
    }

    private async Task Connect(string id)
    {
        var hosts = Preview(views.Find(id));
        if (hosts.Count == 0) return;
        var snapshot = hosts.Select(host => new BatchTarget(host).CreateHost()).ToList();
        console.Write("Connect this target snapshot? (yes/N): ");
        if (Confirmed()) await connections.ConnectAllMultiTab(snapshot);
    }

    private IReadOnlyList<ServerHost> Preview(TargetViewDefinition view)
    {
        var hosts = views.Resolve(view.Id);
        console.WriteLine($"{BatchReportFormatter.Safe(view.Name)}: {BatchReportFormatter.Safe(view.Expression)}; {hosts.Count} target(s).");
        foreach (var host in hosts) console.WriteLine($"  {BatchReportFormatter.Safe(host.Alias)} ({host.Id})");
        if (hosts.Count == 0) console.WriteLine("The target selection is empty. No operation was started.");
        return hosts;
    }

    private bool Confirmed() => console.ReadLine()?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
}
