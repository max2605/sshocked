namespace Sshocked.Models;

public sealed record BatchReport(string PlanId, BatchOperationKind Operation, DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc, IReadOnlyList<BatchTargetResult> Results, string? RetryOf = null)
{
    public bool Success => Results.Count > 0 && Results.All(result => result.Status == BatchTargetStatus.Success);
}
