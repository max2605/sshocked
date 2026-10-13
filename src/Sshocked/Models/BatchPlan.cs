namespace Sshocked.Models;

public sealed class BatchPlan(BatchOperation operation, IEnumerable<BatchTarget> targets, int concurrency,
    string? retryOf = null)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public BatchOperation Operation { get; } = operation;
    public IReadOnlyList<BatchTarget> Targets { get; } = Array.AsReadOnly(targets.ToArray());
    public int Concurrency { get; } = concurrency;
    public string? RetryOf { get; } = retryOf;
}
