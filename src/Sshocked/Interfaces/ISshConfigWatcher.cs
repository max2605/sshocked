namespace Sshocked.Interfaces;

public interface ISshConfigWatcher : IDisposable
{
    event EventHandler? Changed;
    bool IsWatching { get; }
    void StartWatching(IEnumerable<string> paths);
    void StopWatching();
}
