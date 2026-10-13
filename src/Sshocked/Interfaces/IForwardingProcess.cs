namespace Sshocked.Interfaces;

public interface IForwardingProcess : IAsyncDisposable
{
    int Id { get; }
    int? ExitCode { get; }
    string? Error => null;
    Task Completion { get; }
    Task WaitForReady(CancellationToken cancellationToken = default);
    Task Stop();
}
