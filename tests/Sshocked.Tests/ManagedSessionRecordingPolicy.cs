using System.Diagnostics;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class ManagedSessionRecordingPolicy(bool enabled = false) : ISessionRecordingService
{
    public bool ShouldRecord(ServerHost host) => enabled;
    public Task<int> Record(ProcessStartInfo startInfo, ServerHost host, CancellationToken cancellationToken = default, Func<Task>? onStarted = null)
        => throw new NotSupportedException();
}
