namespace Sshocked.Services;

internal static class DockerBatchRunner
{
    public static async Task<IReadOnlyList<TResult>> Run<TTarget, TResult>(IReadOnlyList<TTarget> targets, int maxConcurrency,
        Func<TTarget, Task<TResult>> execute, Func<TTarget, TResult> cancelled, CancellationToken ct)
    {
        if (maxConcurrency is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Concurrency must be between 1 and 16.");
        using var gate = new SemaphoreSlim(maxConcurrency);
        return await Task.WhenAll(targets.Select(async target =>
        {
            var acquired = false;
            try
            {
                await gate.WaitAsync(ct);
                acquired = true;
                ct.ThrowIfCancellationRequested();
                return await execute(target);
            }
            catch (OperationCanceledException) { return cancelled(target); }
            finally { if (acquired) gate.Release(); }
        }));
    }
}
