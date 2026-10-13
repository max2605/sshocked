using Sshocked.Interfaces;

namespace Sshocked.Tests;

internal sealed class ForwardingFakeProcess(bool ready = true) : IForwardingProcess
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Id => 12345;
    public int? ExitCode { get; private set; }
    public Task Completion => _completion.Task;
    public bool IsDisposed { get; private set; }
    public int StopCalls { get; private set; }
    public Task WaitForReady(CancellationToken cancellationToken = default) => ready
        ? Task.CompletedTask : _ready.Task.WaitAsync(cancellationToken);
    public void FailStartup() => _ready.TrySetException(new InvalidOperationException("SSH host-key verification failed."));
    public void Fail(int exit = 17) { ExitCode = exit; _completion.TrySetResult(); }
    public Task Stop() { StopCalls++; ExitCode ??= 0; _completion.TrySetResult(); return Completion; }
    public async ValueTask DisposeAsync() { await Stop(); IsDisposed = true; }
}
