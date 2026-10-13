using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IBatchPlanService
{
    BatchPlan Create(BatchOperation operation, IEnumerable<ServerHost> hosts, int concurrency = 4);
    BatchPlan RetryFailed(BatchPlan original, BatchReport report, IReadOnlyCollection<string> hostIds);
}
