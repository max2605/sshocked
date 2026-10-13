using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MacroMenuService(IMacroService macros, IManagedCommandSessionService commandSession,
    IConfigRepository repository, IMenuFactory menus, IConsoleWriterService console, IConsoleHelperService helper) : IMacroMenuService
{
    public async Task Show()
    {
        var running = true;
        while (running)
        {
            var count = macros.List().Count;
            var entries = new List<MenuEntry>
            {
                new("Record commands on a server", 'R', executeAsync: Record),
                new($"Browse saved macros ({count})", 'M', executeAsync: Browse),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            running = await menus.RunMenu("[bold]Macros[/]", entries);
        }
    }

    public async Task Run(IReadOnlyList<ServerHost> hosts)
    {
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
                { var hosts = SelectTargets(); if (hosts.Count > 0) await RunSelected(macro, hosts); }),
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
        if (items.Count == 0) { console.WriteLine("No macros saved. Start a command session and use :record, :stop, :save NAME."); helper.WaitForKey(); return null; }
        var chosen = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Choose a macro:")
            .UseConverter(Markup.Escape)
            .AddChoices(items.Select(macro => $"{macro.Name} ({macro.Commands.Count} commands, {macro.Id})").Append("Back")));
        return items.FirstOrDefault(macro => chosen == $"{macro.Name} ({macro.Commands.Count} commands, {macro.Id})");
    }

    private IReadOnlyList<ServerHost> SelectTargets()
    {
        var config = repository.Load();
        var targets = new Dictionary<string, IReadOnlyList<ServerHost>>();
        foreach (var host in config.Hosts) targets.Add($"Server: {host.Alias} ({host.Id})", [host]);
        foreach (var group in config.Groups) targets.Add($"Group: {group.Name} ({group.Id})", config.Hosts.Where(host => host.GroupId == group.Id).ToList());
        foreach (var tag in config.Hosts.SelectMany(host => host.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            targets.Add($"Tag: {tag}", config.Hosts.Where(host => host.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList());
        var selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Macro targets:").UseConverter(Markup.Escape).AddChoices(targets.Keys.Append("Back")));
        return targets.GetValueOrDefault(selected) ?? [];
    }

    private async Task RunSelected(MacroDefinition macro, IReadOnlyList<ServerHost> hosts)
    {
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
