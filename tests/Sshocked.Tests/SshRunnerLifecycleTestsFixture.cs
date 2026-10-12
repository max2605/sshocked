using Microsoft.Extensions.Logging.Abstractions;

using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

internal sealed class SshRunnerLifecycleTestsFixture
{
    public List<string> Events { get; } = [];
    public ServerHost Host { get; } = new()
    {
        Id = "host",
        Alias = "web",
        HostName = "example",
        User = "alice",
        GroupId = "prod"
    };
    public SshRunnerLifecycleTestsProcesses Process { get; }
    public SshRunnerLifecycleTestsHooks Hooks { get; }
    public SshRunnerLifecycleTestsAudit Audit { get; }
    public SshRunnerLifecycleTestsRecents Recents { get; }
    public SshRunnerService Runner { get; }

    public SshRunnerLifecycleTestsFixture()
    {
        Process = new(Events);
        Hooks = new(Events);
        Audit = new(Events);
        Recents = new(Events);
        Runner = new(Process, Recents, Audit, Hooks, new SshRunnerLifecycleTestsConfig(), NullLogger<SshRunnerService>.Instance);
    }
}
