using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MacroMenuService(IMacroService macros, IManagedCommandSessionService commandSession,
    IConfigRepository repository, IMenuFactory menus, IConsoleWriterService console, IConsoleHelperService helper,
    IMacroEditorService? editor = null, IBatchMenuService? batches = null) : IMacroMenuService
{
    private readonly IMacroEditorService macroEditor = editor ?? new MacroEditorService(macros, menus, console, helper);

    public async Task Show()
    {
        var running = true;
        while (running)
        {
            var count = macros.List().Count;
            var entries = new List<MenuEntry>
            {
                new(MacroEditorLabels.Create, 'C', executeAsync: async () => { await macroEditor.Create(); }),
                new(MacroEditorLabels.FromTemplate, 'T', executeAsync: async () => { await macroEditor.CreateFromTemplate(); }),
                new("Record commands on a server", 'R', executeAsync: Record),
                new($"Browse saved macros ({count})", 'M', executeAsync: Browse),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            running = await menus.RunMenu("[bold]Macros[/]", entries);
        }
    }

    public async Task Run(IReadOnlyList<ServerHost> hosts)
    {
        if (batches is not null)
        {
            var ids = hosts.Select(host => host.Id).ToHashSet(StringComparer.Ordinal);
            await batches.RunMacro(() => repository.Load().Hosts.Where(host => ids.Contains(host.Id)).ToList());
            return;
        }
        var macro = SelectMacro();
        if (macro is not null) await RunSelected(macro, hosts);
    }

    private async Task Record()
    {
        var hosts = repository.Load().Hosts;
        if (hosts.Count == 0) { console.WriteLine("No servers configured."); helper.WaitForKey(); return; }
        var chosen = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Record commands on which server?")
            .UseConverter(Markup.Escape)
            .AddChoices(hosts.Select(host => $"{host.Alias} ({host.Id})").Append("Back")));
        if (chosen == "Back") return;
        await commandSession.Show(hosts.First(host => chosen == $"{host.Alias} ({host.Id})"));
    }

    private async Task Browse()
    {
        var macro = SelectMacro();
        if (macro is null) return;
        var running = true;
        while (running)
        {
            running = await menus.RunMenu($"[bold]Macro: {Markup.Escape(macro.Name)}[/]", [
                new("View command sequence", 'V', executeAsync: () =>
                {
                    for (var i = 0; i < macro.Commands.Count; i++) console.WriteLine($"{i + 1}. {macro.Commands[i]}");
                    helper.WaitForKey(); return Task.CompletedTask;
                }),
                new("Run on server, group or tag", 'R', executeAsync: async () =>
                { if (SelectTargets() is { } targets) await RunSelected(macro, targets); }),
                new(MacroEditorLabels.Edit, 'E', executeAsync: async () =>
                { if (await macroEditor.Edit(macro) is { } saved) macro = saved; }),
                new(MacroEditorLabels.Duplicate, 'C', executeAsync: async () => { await macroEditor.Duplicate(macro); }),
                new("Delete macro", 'D', executeAsync: () =>
                {
                    if (AnsiConsole.Confirm($"Delete macro '{Markup.Escape(macro.Name)}'?", false))
                    { macros.Delete(macro.Id); running = false; }
                    return Task.CompletedTask;
                }),
                new("Back", 'B', actionType: MenuActionType.Back)
            ]) && running;
        }
    }

    private MacroDefinition? SelectMacro()
    {
        var items = macros.List();
        if (items.Count == 0) { console.WriteLine("No macros saved. Create a macro, use a template, or record commands on a server."); helper.WaitForKey(); return null; }
        var chosen = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Choose a macro:")
            .UseConverter(Markup.Escape)
            .AddChoices(items.Select(macro => $"{macro.Name} ({macro.Commands.Count} commands, {macro.Id})").Append("Back")));
        return items.FirstOrDefault(macro => chosen == $"{macro.Name} ({macro.Commands.Count} commands, {macro.Id})");
    }

    private Func<IReadOnlyList<ServerHost>>? SelectTargets()
    {
        var config = repository.Load();
        var targets = new Dictionary<string, Func<IReadOnlyList<ServerHost>>>();
        foreach (var host in config.Hosts)
            targets.Add($"Server: {host.Alias} ({host.Id})", () => repository.Load().Hosts.Where(current => current.Id == host.Id).ToList());
        foreach (var group in config.Groups)
            targets.Add($"Group: {group.Name} ({group.Id})", () => repository.Load().Hosts.Where(host => host.GroupId == group.Id).ToList());
        foreach (var tag in config.Hosts.SelectMany(host => host.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            targets.Add($"Tag: {tag}", () => repository.Load().Hosts.Where(host => host.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList());
        var selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Macro targets:").UseConverter(Markup.Escape).AddChoices(targets.Keys.Append("Back")));
        return targets.GetValueOrDefault(selected);
    }

    private Task RunSelected(MacroDefinition macro, IReadOnlyList<ServerHost> hosts)
    {
        var ids = hosts.Select(host => host.Id).ToHashSet(StringComparer.Ordinal);
        return RunSelected(macro, () => repository.Load().Hosts.Where(host => ids.Contains(host.Id)).ToList());
    }

    private async Task RunSelected(MacroDefinition macro, Func<IReadOnlyList<ServerHost>> targets)
    {
        if (batches is not null) { await batches.RunMacro(targets, macro); return; }
        var hosts = targets();
        if (hosts.Count == 0) { console.WriteLine("No servers in the selected target."); helper.WaitForKey(); return; }
        console.WriteLine($"Macro '{macro.Name}' will run {macro.Commands.Count} commands on: {string.Join(", ", hosts.Select(host => host.Alias))}.");
        if (!AnsiConsole.Confirm("Run this macro?", false)) return;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var results = await macros.Run(macro, hosts, console.Write, cancellation.Token);
            console.WriteLine($"Completed: {results.Count(result => result.Success)}/{results.Count} servers succeeded.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { console.WriteLine("Macro execution cancelled."); }
        finally { Console.CancelKeyPress -= cancel; }
        helper.WaitForKey();
    }
}
