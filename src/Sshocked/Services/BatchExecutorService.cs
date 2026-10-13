using System.Diagnostics;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class BatchExecutorService(IProcessService process, IMacroService macros,
    IFileTransferService transfers) : IBatchExecutorService
{
    public async Task<BatchReport> Execute(BatchPlan plan, Action<BatchTargetResult>? progress = null,
        CancellationToken cancellationToken = default, Action<BatchTarget, string>? output = null)
    {
        if (plan.Concurrency is < 1 or > 32 || plan.Targets.Count == 0) throw new ArgumentException("Invalid batch plan.");
        var started = DateTimeOffset.UtcNow;
        var results = plan.Targets.Select(target => new BatchTargetResult(target.HostId, target.Alias, BatchTargetStatus.NotStarted)).ToArray();
        var gate = new object();
        var next = 0;
        async Task Work()
        {
            while (true)
            {
                int index;
                lock (gate)
                {
                    if (cancellationToken.IsCancellationRequested || next >= plan.Targets.Count) return;
                    index = next++;
                }
                var target = plan.Targets[index];
                var timer = Stopwatch.StartNew();
                BatchTargetResult result;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    void Emit(string text)
                    {
                        if (output is null || text.Length == 0) return;
                        lock (gate) output(target, text);
                    }
                    var exit = await ExecuteTarget(plan.Operation, target, cancellationToken, Emit);
                    result = new(target.HostId, target.Alias, exit == 0 ? BatchTargetStatus.Success : BatchTargetStatus.Failed,
                        timer.Elapsed.TotalSeconds, exit, exit == 0 ? null : "The operation returned a nonzero exit code.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                { result = new(target.HostId, target.Alias, BatchTargetStatus.Cancelled, timer.Elapsed.TotalSeconds, Error: "Execution cancelled."); }
                catch (Exception exception)
                { result = new(target.HostId, target.Alias, BatchTargetStatus.Failed, timer.Elapsed.TotalSeconds, Error: exception.GetType().Name); }
                lock (gate)
                {
                    results[index] = result;
                    progress?.Invoke(result);
                }
            }
        }
        await Task.WhenAll(Enumerable.Range(0, Math.Min(plan.Concurrency, plan.Targets.Count)).Select(_ => Work()));
        return new(plan.Id, plan.Operation.Kind, started, DateTimeOffset.UtcNow, Array.AsReadOnly(results), plan.RetryOf);
    }

    private async Task<int> ExecuteTarget(BatchOperation operation, BatchTarget target, CancellationToken ct, Action<string> output)
    {
        var host = target.CreateHost();
        switch (operation.Kind)
        {
            case BatchOperationKind.Command:
                var execution = await process.RunSshCommandResultNonInteractive(host, operation.Command!, ct);
                output(execution.StandardOutput);
                output(execution.StandardError);
                return execution.ExitCode;
            case BatchOperationKind.Macro:
                var result = (await macros.RunNonInteractive(operation.GetMacro(), [host], output, ct)).Single();
                if (!result.Success && result.ExitCode is null) throw new InvalidOperationException("Macro execution failed.");
                return result.ExitCode ?? (result.Success ? 0 : 1);
            case BatchOperationKind.Upload:
                return await transfers.UploadFileNonInteractive(host, operation.LocalPath!, operation.RemotePath!, operation.Recursive, ct);
            case BatchOperationKind.Download:
                return await transfers.DownloadFileNonInteractive(host, operation.RemotePath!, target.LocalPath!, operation.Recursive, ct);
            default: throw new ArgumentException("Unsupported batch operation.");
        }
    }
}
