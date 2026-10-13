using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class BatchPlanService : IBatchPlanService
{
    public BatchPlan Create(BatchOperation operation, IEnumerable<ServerHost> hosts, int concurrency = 4)
    {
        if (concurrency is < 1 or > 32) throw new ArgumentException("Batch concurrency must be from 1 to 32.");
        var selected = hosts.DistinctBy(host => host.Id, StringComparer.Ordinal).ToList();
        if (selected.Count == 0) throw new ArgumentException("The target selection is empty. No operation was started.");
        if (selected.Any(host => string.IsNullOrWhiteSpace(host.Id))) throw new ArgumentException("Every batch target needs a stable host ID.");
        Validate(operation);
        var targets = operation.Kind == BatchOperationKind.Download
            ? FileTransferService.BuildGroupDownloadPaths(selected, operation.RemotePath!, operation.LocalPath!)
                .Select(pair => new BatchTarget(pair.Host, pair.LocalPath)).ToList()
            : selected.Select(host => new BatchTarget(host)).ToList();
        return new(operation, targets, concurrency);
    }

    public BatchPlan RetryFailed(BatchPlan original, BatchReport report, IReadOnlyCollection<string> hostIds)
    {
        if (report.PlanId != original.Id) throw new ArgumentException("The report belongs to another batch plan.");
        if (hostIds.Count == 0) throw new ArgumentException("Explicitly select failed targets to retry.");
        var failed = report.Results.Where(result => result.Status == BatchTargetStatus.Failed)
            .Select(result => result.HostId).ToHashSet(StringComparer.Ordinal);
        var selected = hostIds.ToHashSet(StringComparer.Ordinal);
        if (!selected.IsSubsetOf(failed)) throw new ArgumentException("Only failed targets in this report can be retried.");
        var targets = original.Targets.Where(target => selected.Contains(target.HostId)).ToList();
        if (targets.Count != selected.Count) throw new ArgumentException("A selected failed target is not in the original snapshot.");
        return new(original.Operation, targets, original.Concurrency, original.Id);
    }

    private static void Validate(BatchOperation operation)
    {
        switch (operation.Kind)
        {
            case BatchOperationKind.Command:
                if (string.IsNullOrWhiteSpace(operation.Command) || operation.Command.Contains('\0'))
                    throw new ArgumentException("A batch command is required and cannot contain NUL.");
                break;
            case BatchOperationKind.Macro:
                _ = MacroScriptBuilder.Build(operation.GetMacro());
                break;
            case BatchOperationKind.Upload:
                if (string.IsNullOrWhiteSpace(operation.LocalPath)) throw new ArgumentException("A local upload path is required.");
                if (!File.Exists(operation.LocalPath) && !(operation.Recursive && Directory.Exists(operation.LocalPath)))
                    throw new ArgumentException("Upload source does not exist, or its directory needs --recursive.");
                ScpArguments.ValidatePath(operation.RemotePath!);
                break;
            case BatchOperationKind.Download:
                if (string.IsNullOrWhiteSpace(operation.LocalPath)) throw new ArgumentException("A local destination directory is required.");
                ScpArguments.ValidatePath(operation.RemotePath!);
                break;
            default: throw new ArgumentException("Unsupported batch operation.");
        }
    }
}
