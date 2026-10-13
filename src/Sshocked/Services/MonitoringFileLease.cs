namespace Sshocked.Services;

internal sealed class MonitoringFileLease(FileStream stream) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
