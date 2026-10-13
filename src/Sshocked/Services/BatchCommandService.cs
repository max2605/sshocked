using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class BatchCommandService(IBatchPlanService plans, IBatchExecutorService executor,
    BatchTargetResolver targets, IMacroService macros, IConsoleWriterService console) : IManagementCommandHandler
{
    public bool Handles(string command) => command.Equals("batch", StringComparison.OrdinalIgnoreCase);

    public async Task Execute(string command, string[] arguments)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? throw new ArgumentException(Usage);
        var options = new CommandOptions(arguments[1..], ["server", "group", "tag", "view", "concurrency", "local", "remote", "output", "report", "retry-report", "failed"],
            ["execute", "recursive"]);
        var output = options.Get("output", "table")!;
        if (output is not ("table" or "json" or "csv")) throw new ArgumentException("Use --output table|json|csv.");
        var operation = Operation(action, options);
        IReadOnlyList<ServerHost> hosts;
        if (options.Get("retry-report") is { } reportPath)
        {
            if (new[] { "server", "group", "tag", "view" }.Any(options.Has)) throw new ArgumentException("Retry selects targets from --retry-report, not target filters.");
            if (BatchReportFormatter.Read(reportPath).Operation != operation.Kind) throw new ArgumentException("The retry operation kind must match the original report.");
            hosts = targets.RetryTargets(reportPath, options.Get("failed") ?? throw new ArgumentException("Select failed targets with --failed ID,ID."));
        }
        else
        {
            if (options.Has("failed")) throw new ArgumentException("--failed requires --retry-report.");
            hosts = targets.Resolve(options);
        }
        var plan = plans.Create(operation, hosts, options.Integer("concurrency", operation.Kind is BatchOperationKind.Upload or BatchOperationKind.Download ? 1 : 4, 1, 32));
        if (!options.Has("execute"))
        {
            Preview(plan);
            console.WriteLine("Preview only. Repeat with --execute to run this operation.");
            return;
        }
        if (output == "table") Preview(plan);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var report = await executor.Execute(plan, cancellationToken: cancellation.Token,
                output: output == "table" ? (target, text) => BatchTerminalOutput.Write(console, target, text) : null);
            console.WriteLine(BatchReportFormatter.Format(report, output));
            if (options.Get("report") is { } path) BatchReportFormatter.Write(report, path);
            if (!report.Success) Environment.ExitCode = cancellation.IsCancellationRequested ? 130 : 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private BatchOperation Operation(string action, CommandOptions options)
    {
        if (action is "command" or "macro")
        {
            if (options.Positionals.Count != 1 || options.Has("local") || options.Has("remote") || options.Has("recursive")) throw new ArgumentException(Usage);
            return action == "command" ? BatchOperation.ForCommand(options.Positionals[0]) : BatchOperation.ForMacro(macros.Find(options.Positionals[0]));
        }
        if (options.Positionals.Count != 0) throw new ArgumentException(Usage);
        var local = options.Get("local") ?? throw new ArgumentException("Transfers require --local PATH.");
        var remote = options.Get("remote") ?? throw new ArgumentException("Transfers require --remote PATH.");
        local = Path.GetFullPath(Environment.ExpandEnvironmentVariables(local));
        return action switch
        {
            "upload" => BatchOperation.ForUpload(local, remote, options.Has("recursive")),
            "download" => BatchOperation.ForDownload(remote, local, options.Has("recursive")),
            _ => throw new ArgumentException(Usage)
        };
    }

    private void Preview(BatchPlan plan)
    {
        console.WriteLine($"Batch preview: {plan.Operation.Kind}; {plan.Targets.Count} target(s); concurrency {plan.Concurrency}.");
        if (plan.Operation.Kind == BatchOperationKind.Command) console.WriteLine("Command: " + BatchReportFormatter.Safe(plan.Operation.Command!));
        if (plan.Operation.Kind == BatchOperationKind.Macro) console.WriteLine("Macro: " + BatchReportFormatter.Safe(plan.Operation.GetMacro().Name));
        if (plan.Operation.Kind is BatchOperationKind.Upload or BatchOperationKind.Download)
            console.WriteLine($"Paths: {BatchReportFormatter.Safe(plan.Operation.LocalPath!)} <-> {BatchReportFormatter.Safe(plan.Operation.RemotePath!)}");
        foreach (var target in plan.Targets) console.WriteLine($"  {BatchReportFormatter.Safe(target.Alias)} ({target.HostId})");
    }

    private const string Usage = "Use batch command COMMAND|macro NAME|upload|download --server ALIAS|--group NAME|--tag TAG|--view NAME [--local PATH --remote PATH] [--concurrency N] [--execute] [--output table|json|csv] [--report FILE].";
}
