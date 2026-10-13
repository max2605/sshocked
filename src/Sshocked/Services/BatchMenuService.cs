using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class BatchMenuService(IBatchPlanService plans, IBatchExecutorService executor,
    IMacroService macros, IConfigRepository repository, ITargetViewService views,
    IConsoleWriterService console, IMenuFactory menus, IPathInputService paths) : IBatchMenuService
{
    public async Task Show()
    {
        while (true)
        {
            var config = repository.Load();
            var entries = new List<MenuEntry>
            {
                new("All servers", executeAsync: () => Actions(() => repository.Load().Hosts))
            };
            entries.AddRange(config.Groups.Select(group => new MenuEntry("Group: " + Markup.Escape(group.Name),
                executeAsync: () => Actions(() => repository.Load().Hosts.Where(host => host.GroupId == group.Id).ToList()))));
            entries.AddRange(views.List().Select(view => new MenuEntry("View: " + Markup.Escape(view.Name),
                executeAsync: () => Actions(() => views.Resolve(view.Id)))));
            entries.Add(new("Back", 'B', actionType: MenuActionType.Back));
            if (!await menus.RunMenu("Batch target selection", entries)) return;
        }
    }

    private async Task Actions(Func<IReadOnlyList<ServerHost>> targets)
    {
        var entries = new List<MenuEntry>
        {
            new("Run command", 'R', executeAsync: () => RunCommand(targets)),
            new("Run macro", 'M', executeAsync: () => RunMacro(targets)),
            new("Upload files", 'U', executeAsync: () => Upload(targets)),
            new("Download files", 'D', executeAsync: () => Download(targets)),
            new("Back", 'B', actionType: MenuActionType.Back)
        };
        while (await menus.RunMenu("Batch operations", entries)) { }
    }

    public async Task RunCommand(Func<IReadOnlyList<ServerHost>> targets)
    {
        console.Write("Command (blank cancels): ");
        var command = console.ReadLine();
        if (string.IsNullOrWhiteSpace(command)) return;
        await Run(BatchOperation.ForCommand(command), targets, 4);
    }

    public async Task RunMacro(Func<IReadOnlyList<ServerHost>> targets)
    {
        foreach (var macro in macros.List()) console.WriteLine($"{macro.Id} {macro.Name} ({macro.Commands.Count} commands)");
        console.Write("Macro name or ID (blank cancels): ");
        var name = console.ReadLine();
        if (string.IsNullOrWhiteSpace(name)) return;
        await Run(BatchOperation.ForMacro(macros.Find(name)), targets, 4);
    }

    public Task RunMacro(Func<IReadOnlyList<ServerHost>> targets, MacroDefinition macro) =>
        Run(BatchOperation.ForMacro(macro), targets, 4);

    public async Task Upload(Func<IReadOnlyList<ServerHost>> targets)
    {
        var local = paths.Prompt("Local file or directory", validate: value => File.Exists(value) || Directory.Exists(value)
            ? null : "The upload source does not exist.");
        if (local is null) return;
        var remote = paths.Prompt("Remote destination on every server", "~/");
        if (remote is null) return;
        await Run(BatchOperation.ForUpload(local, remote, Directory.Exists(local)), targets, 1);
    }

    public async Task Download(Func<IReadOnlyList<ServerHost>> targets)
    {
        var remote = paths.Prompt("Remote file or directory on every server");
        if (remote is null) return;
        var local = paths.Prompt("Local destination directory", validate: value => Directory.Exists(value)
            ? null : "The destination directory does not exist.");
        if (local is null) return;
        console.Write("Download a directory recursively? (yes/N): ");
        var recursive = Confirm();
        await Run(BatchOperation.ForDownload(remote, local, recursive), targets, 1);
    }

    private async Task Run(BatchOperation operation, Func<IReadOnlyList<ServerHost>> targets, int defaultConcurrency)
    {
        console.Write($"Concurrency 1-32 (blank uses {defaultConcurrency}): ");
        var input = console.ReadLine();
        var concurrency = string.IsNullOrWhiteSpace(input) ? defaultConcurrency : int.TryParse(input, out var value) ? value : 0;
        await Execute(plans.Create(operation, targets(), concurrency));
    }

    private async Task Execute(BatchPlan plan)
    {
        while (true)
        {
            Preview(plan);
            console.Write("Execute this snapshot? (yes/N): ");
            if (!Confirm()) return;
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            BatchReport report;
            try
            {
                report = await executor.Execute(plan, result => console.WriteLine($"{BatchReportFormatter.Safe(result.Alias)}: {result.Status}"), cancellation.Token,
                    (target, text) => BatchTerminalOutput.Write(console, target, text));
            }
            finally { Console.CancelKeyPress -= cancel; }
            console.WriteLine(BatchReportFormatter.Format(report, "table"));
            console.Write("Save JSON report path (blank skips): ");
            var path = console.ReadLine();
            if (!string.IsNullOrWhiteSpace(path)) BatchReportFormatter.Write(report, path);
            var failed = report.Results.Where(result => result.Status == BatchTargetStatus.Failed).ToList();
            if (failed.Count == 0) return;
            console.WriteLine("Failed target IDs: " + string.Join(", ", failed.Select(result => result.HostId)));
            console.Write("Retry selected failed IDs, comma separated (blank returns): ");
            var selected = console.ReadLine();
            if (string.IsNullOrWhiteSpace(selected)) return;
            plan = plans.RetryFailed(plan, report, selected.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }
    }

    private void Preview(BatchPlan plan)
    {
        console.WriteLine($"Batch preview: {plan.Operation.Kind}; {plan.Targets.Count} target(s); concurrency {plan.Concurrency}.");
        if (plan.Operation.Kind == BatchOperationKind.Command) console.WriteLine("Command: " + BatchReportFormatter.Safe(plan.Operation.Command!));
        if (plan.Operation.Kind == BatchOperationKind.Macro) console.WriteLine("Macro: " + BatchReportFormatter.Safe(plan.Operation.GetMacro().Name));
        if (plan.Operation.Kind is BatchOperationKind.Upload or BatchOperationKind.Download)
            console.WriteLine($"Paths: {BatchReportFormatter.Safe(plan.Operation.LocalPath!)} <-> {BatchReportFormatter.Safe(plan.Operation.RemotePath!)}");
        foreach (var target in plan.Targets)
            console.WriteLine($"  {BatchReportFormatter.Safe(target.Alias)} ({target.HostId}){(target.LocalPath is null ? "" : " -> " + BatchReportFormatter.Safe(target.LocalPath))}");
    }

    private bool Confirm() => console.ReadLine()?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
}
