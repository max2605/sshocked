using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IBatchExecutorService
{
    Task<BatchReport> Execute(BatchPlan plan, Action<BatchTargetResult>? progress = null,
        CancellationToken cancellationToken = default, Action<BatchTarget, string>? output = null);
}
