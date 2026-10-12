namespace Sshocked.Interfaces;

public interface IBastionTunnel : IAsyncDisposable
{
    int LocalPort { get; }

    bool IsReady { get; }

    Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken ct = default);
}
