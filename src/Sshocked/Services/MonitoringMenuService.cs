using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MonitoringMenuService(IMonitoringService monitoring, IMonitoringStore store,
    IConfigRepository repository, IMenuFactory menu, IConsoleWriterService console,
    IConsoleHelperService helper) : IMonitoringMenuService
{
    public async Task Show()
    {
        var running = true;
        while (running)
        {
            var enabled = repository.Load().Monitoring.Enabled;
            var entries = new List<MenuEntry>
            {
                new("Show current snapshots", 'S', executeAsync: () => { ShowResults(); return Task.CompletedTask; }),
                new("Check all enabled monitors now", 'R', executeAsync: async () =>
                { MonitoringDisplay.Write(console, await monitoring.Run()); helper.WaitForKey(); }),
                new(enabled ? "Disable continuous monitoring" : "Enable continuous monitoring", 'E', executeAsync: () =>
                { repository.Update(config => config.Monitoring.Enabled = !enabled); return Task.CompletedTask; }),
                new("Watch (Ctrl+C returns)", 'W', executeAsync: Watch),
                new("Create monitor", 'C', executeAsync: () => Edit(null)),
                new("Manage definitions", 'M', executeAsync: Browse),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            running = await menu.RunMenu("[bold]Monitoring[/]", entries);
        }
    }

    private void ShowResults()
    {
        MonitoringDisplay.Write(console, monitoring.Status());
        helper.WaitForKey();
    }

    private async Task Browse()
    {
        var definitions = monitoring.Definitions();
        if (definitions.Count == 0) { console.WriteLine("No monitors defined."); helper.WaitForKey(); return; }
        var chosen = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Select a monitor:").UseConverter(Markup.Escape)
            .AddChoices(definitions.Select(item => $"{item.Name} ({item.Id})").Append("Back")));
        var selected = definitions.FirstOrDefault(item => chosen == $"{item.Name} ({item.Id})");
        if (selected is null) return;
        var running = true;
        while (running)
        {
            var definition = monitoring.Definitions().FirstOrDefault(item => item.Id == selected.Id);
            if (definition is null) return;
            var entries = new List<MenuEntry> {
                new("Edit", 'E', executeAsync: () => Edit(definition)),
                new("Check now", 'R', executeAsync: async () =>
                { MonitoringDisplay.Write(console, [await monitoring.RunOne(definition.Id)]); helper.WaitForKey(); }),
                new("Show alarms and history", 'H', executeAsync: () => { Details(definition); return Task.CompletedTask; }),
                new("Acknowledge alarm", 'A', executeAsync: () => Acknowledge(definition)),
                new("Delete", 'D', executeAsync: async () =>
                {
                    if (AnsiConsole.Confirm("Delete this monitoring definition?", false))
                    { await monitoring.Delete(definition.Id); running = false; }
                }),
                new("Back", 'B', actionType: MenuActionType.Back)
            };
            if (definition.Provider == "log") entries.Insert(entries.Count - 1, new("Test pattern locally", 'T', executeAsync: () =>
            {
                var lines = new List<string>();
                while (lines.Count < 1000)
                {
                    var line = AnsiConsole.Prompt(new TextPrompt<string>("Example line (empty ends input):").AllowEmpty());
                    if (line.Length == 0) break;
                    lines.Add(line);
                }
                var matches = LogMonitoringProvider.TestPatternLines(definition, lines);
                console.WriteLine($"{matches.Count} / {lines.Count} lines match. No remote command executed.");
                foreach (var match in matches) console.WriteLine(MonitoringDisplay.Literal(match));
                helper.WaitForKey(); return Task.CompletedTask;
            }));
            running = await menu.RunMenu($"[bold]{Markup.Escape(definition.Name)}[/]", entries) && running;
        }
    }

    private async Task Edit(MonitorDefinition? original)
    {
        var definition = original is null ? new MonitorDefinition() :
            JsonSerializer.Deserialize(JsonSerializer.Serialize(original, MonitoringJsonContext.Default.MonitorDefinition), MonitoringJsonContext.Default.MonitorDefinition)!;
        var name = AnsiConsole.Prompt(new TextPrompt<string>("Monitor name (empty cancels):").DefaultValue(definition.Name).AllowEmpty());
        if (string.IsNullOrWhiteSpace(name)) return;
        definition.Name = name;
        if (original is null)
        {
            var hosts = repository.Load().Hosts;
            if (hosts.Count == 0) throw new ArgumentException("Configure a host before adding a monitor.");
            var host = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Host:").UseConverter(Markup.Escape)
                .AddChoices(hosts.Select(item => $"{item.Alias} ({item.Id})")));
            definition.HostId = hosts.Single(item => host == $"{item.Alias} ({item.Id})").Id;
            definition.Provider = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Provider:").AddChoices(monitoring.Providers.Select(item => item.Key)));
            if (definition.Provider == "smart") { definition.IntervalSeconds = 3600; definition.FreshnessSeconds = 7200; }
            if (definition.Provider == "updates") { definition.IntervalSeconds = 21600; definition.FreshnessSeconds = 43200; definition.TimeoutSeconds = 300; }
        }
        definition.Enabled = AnsiConsole.Confirm("Enable this monitor?", definition.Enabled);
        definition.IntervalSeconds = Integer("Poll interval (seconds):", definition.IntervalSeconds);
        definition.TimeoutSeconds = Integer("Probe timeout (seconds):", definition.TimeoutSeconds);
        definition.FreshnessSeconds = Integer("Maximum fresh result age (seconds):", definition.FreshnessSeconds);
        definition.GraceSeconds = Integer("Alarm hold/grace (seconds):", definition.GraceSeconds);
        definition.ConsecutiveSamples = Integer("Required consecutive alarm samples:", definition.ConsecutiveSamples);
        var provider = monitoring.Providers.Single(item => item.Key == definition.Provider);
        foreach (var option in provider.Options)
        {
            var previous = MonitoringOptions.Get(definition, option.Key, option.DefaultValue);
            definition.Options[option.Key] = option.Choices is { Length: > 0 }
                ? AnsiConsole.Prompt(new SelectionPrompt<string>().Title(Markup.Escape(option.Prompt)).AddChoices(option.Choices).DefaultValue(previous))
                : AnsiConsole.Prompt(new TextPrompt<string>(Markup.Escape(option.Prompt) + ":").DefaultValue(previous).AllowEmpty());
        }
        await EditRules(definition);
        console.WriteLine($"Save '{definition.Name}' for host {definition.HostId}, provider {definition.Provider}?");
        if (AnsiConsole.Confirm("Save monitoring definition? No remote command is executed.", false))
            monitoring.Save(definition, original);
    }

    private async Task EditRules(MonitorDefinition definition)
    {
        var running = true;
        while (running)
        {
            running = await menu.RunMenu("[bold]Numeric alarm rules[/]", [
                new($"Add rule ({definition.Rules.Count} configured)", 'A', executeAsync: () =>
                {
                    var rule = new MonitorRule();
                    rule.Name = AnsiConsole.Prompt(new TextPrompt<string>("Rule name:"));
                    rule.MetricKey = AnsiConsole.Prompt(new TextPrompt<string>("Metric key (see a probe snapshot):"));
                    rule.Comparison = AnsiConsole.Prompt(new SelectionPrompt<MonitorComparison>().Title("Comparison:").AddChoices(Enum.GetValues<MonitorComparison>()));
                    rule.Threshold = Number("Threshold:");
                    if (AnsiConsole.Confirm("Set a different clear threshold (hysteresis)?", false)) rule.ClearThreshold = Number("Clear threshold:");
                    rule.Severity = AnsiConsole.Prompt(new SelectionPrompt<MonitorSeverity>().Title("Severity:").AddChoices(MonitorSeverity.Warning, MonitorSeverity.Critical));
                    rule.ConsecutiveSamples = Integer("Required consecutive samples:", 1);
                    rule.HoldSeconds = Integer("Hold time (seconds):", 0);
                    definition.Rules.Add(rule); return Task.CompletedTask;
                }),
                new("Remove a rule", 'D', executeAsync: () =>
                {
                    if (definition.Rules.Count == 0) return Task.CompletedTask;
                    var choice = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Remove:").UseConverter(Markup.Escape)
                        .AddChoices(definition.Rules.Select(rule => $"{rule.Name} ({rule.Id})").Append("Back")));
                    definition.Rules.RemoveAll(rule => choice == $"{rule.Name} ({rule.Id})");
                    return Task.CompletedTask;
                }),
                new("Done", 'B', actionType: MenuActionType.Back)
            ]);
        }
    }

    private void Details(MonitorDefinition definition)
    {
        var record = store.Load(definition.Id);
        if (record is null) { console.WriteLine("No probe has completed."); helper.WaitForKey(); return; }
        MonitoringDisplay.Write(console, monitoring.Status().Where(snapshot => snapshot.MonitorId == definition.Id).ToList());
        if (!MonitoringScope.Matches(record, definition, repository.Load().Hosts.FirstOrDefault(host => host.Id == definition.HostId)))
            console.WriteLine("Stored alarm history belongs to an earlier definition or endpoint; run a new probe.");
        foreach (var alarm in record.Alarms)
            console.WriteLine($"{MonitoringDisplay.Literal(alarm.Key)}: active={alarm.Active} acknowledged={alarm.Acknowledged} {alarm.Severity}");
        foreach (var entry in record.Events.TakeLast(20)) console.WriteLine($"{entry.TimestampUtc:O} {entry.Action} {entry.AlarmKey} {entry.Severity}");
        helper.WaitForKey();
    }

    private async Task Acknowledge(MonitorDefinition definition)
    {
        var alarms = store.Load(definition.Id)?.Alarms.Where(item => item.Active && !item.Acknowledged).ToList() ?? [];
        if (alarms.Count == 0) { console.WriteLine("No unacknowledged active alarms."); return; }
        var selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Acknowledge alarm:").UseConverter(Markup.Escape)
            .AddChoices(alarms.Select(item => item.Key).Append("Back")));
        if (selected != "Back") await monitoring.Acknowledge(definition.Id, selected);
    }

    private async Task Watch()
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await monitoring.Watch(snapshots =>
            {
                if (AnsiConsole.Profile.Capabilities.Interactive) AnsiConsole.Clear();
                console.WriteLine("Monitoring watch - Ctrl+C returns. Monitoring stops when this process exits.");
                MonitoringDisplay.Write(console, snapshots);
            }, cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { console.WriteLine("Monitoring stopped."); }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static int Integer(string prompt, int value) => AnsiConsole.Prompt(new TextPrompt<int>(prompt).DefaultValue(value));
    private static double Number(string prompt)
    {
        var text = AnsiConsole.Prompt(new TextPrompt<string>(prompt));
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : throw new ArgumentException("Use a finite invariant decimal value.");
    }
}
