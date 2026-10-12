using Sshocked.Models;

namespace Sshocked.Services;

internal sealed record HostSelectorServiceChoice(ServerHost? Host, string Label);
