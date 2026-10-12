using Sshocked.Interfaces;



namespace Sshocked.Tests;

internal sealed class SshConfigSyncTestsFakeWatcher : ISshConfigWatcher
{
    public event EventHandler? Changed;
    public bool IsWatching { get; private set; }

    public void StartWatching(IEnumerable<string> paths) => IsWatching = true;
    public void StopWatching() => IsWatching = false;
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() => StopWatching();
}
