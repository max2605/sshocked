namespace Sshocked.Services;

internal sealed class ProcessOutputBudget(int maximumBytes)
{
    private long _captured;
    public void Consume(int bytes)
    {
        if (Interlocked.Add(ref _captured, bytes) > maximumBytes) throw new ProcessOutputLimitException(maximumBytes);
    }
}
