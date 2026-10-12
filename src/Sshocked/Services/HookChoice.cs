using Sshocked.Models;

namespace Sshocked.Services;

internal sealed record HookChoice(HookPhase? Phase, string Description);
