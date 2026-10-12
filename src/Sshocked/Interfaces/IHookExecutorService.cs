using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IHookExecutorService
{
    Task<bool> RunLocalHooks(HookPhase phase, ServerHost host, GlobalHooks? globalHooks,
        HookContext context, CancellationToken cancellationToken = default);
    string? BuildRemoteSessionCommand(ServerHost host, GlobalHooks? globalHooks, HookContext context);
}
