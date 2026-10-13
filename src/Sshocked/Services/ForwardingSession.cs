using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class ForwardingSession(ForwardingRuntimeRecord record, IForwardingProcess process,
    ISshConnectionLease credentials, FileStream profileLease, CancellationTokenSource cancellation)
{
    public ForwardingRuntimeRecord Record { get; } = record;
    public IForwardingProcess Process { get; } = process;
    public ISshConnectionLease Credentials { get; } = credentials;
    public FileStream ProfileLease { get; } = profileLease;
    public CancellationTokenSource Cancellation { get; } = cancellation;
    public object Gate { get; } = new();
    public Task Completion { get; set; } = Task.CompletedTask;
    public string? Failure { get; set; }
    public bool StopRequested { get; set; }
}
