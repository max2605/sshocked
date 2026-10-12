using Sshocked.Interfaces;



namespace Sshocked.Tests;

internal sealed class SecretProviderTestsCredentialTunnel : IBastionTunnel
{
    public int LocalPort => 42522;
    public bool IsReady => true;
    public bool Disposed { get; private set; }

    public Task WaitUntilReady(TimeSpan timeout, CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
