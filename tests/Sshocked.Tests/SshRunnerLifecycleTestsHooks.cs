using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SshRunnerLifecycleTestsHooks(List<string> events) : IHookExecutorService
{
    public Dictionary<HookPhase, bool> Results { get; } = [];
    public Dictionary<HookPhase, Exception> Failures { get; } = [];
    public Dictionary<HookPhase, HookContext> Contexts { get; } = [];

    public Task<bool> RunLocalHooks(HookPhase phase, ServerHost host, GlobalHooks? globalHooks, HookContext context, CancellationToken cancellationToken = default)
    {
        events.Add(phase.ToString());
        Contexts[phase] = context;
        if (Failures.TryGetValue(phase, out var failure))
            throw failure;
        return Task.FromResult(Results.GetValueOrDefault(phase, true));
    }

    public string? BuildRemoteSessionCommand(ServerHost host, GlobalHooks? globalHooks, HookContext context)
    {
        events.Add("remote-wrapper");
        return "remote wrapper";
    }
}
