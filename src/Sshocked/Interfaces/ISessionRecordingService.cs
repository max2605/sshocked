using System.Diagnostics;
using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISessionRecordingService
{
    bool ShouldRecord(ServerHost host);
    Task<int> Record(ProcessStartInfo startInfo, ServerHost host, CancellationToken cancellationToken = default, Func<Task>? onStarted = null);
}
