using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SshRunnerLifecycleTestsProcesses(List<string> events) : IProcessService
{
    public int ExitCode { get; set; }
    public Exception? LaunchFailure { get; set; }
    public string? InitialCommand { get; private set; }
    public bool AuditCommand { get; private set; } = true;

    public Task RunSsh(ServerHost host) => throw new NotSupportedException();
    public Task<string> RunSshCommand(ServerHost host, string command) => throw new NotSupportedException();
    public Task RunSshInteractive(ServerHost host, string command) => throw new NotSupportedException();
    public async Task<int> RunSshSession(ServerHost host, string? initialCommand = null, CancellationToken ct = default, Func<Task>? onStarted = null, bool auditCommand = true)
    {
        if (LaunchFailure is not null)
            throw LaunchFailure;
        InitialCommand = initialCommand;
        AuditCommand = auditCommand;
        events.Add("ssh-start");
        try
        {
            if (onStarted is not null)
                await onStarted();
        }
        catch
        {
            events.Add("ssh-aborted");
            throw;
        }

        events.Add("ssh-exit");
        return ExitCode;
    }
}
