using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SshRunnerService(IProcessService processService, IRecentConnectionService recents, ILogger<SshRunnerService> logger) : ISshRunnerService
{
    public async Task Connect(ServerHost host)
    {
        logger.LogInformation("Connecting to {Alias}", host.Alias);
        await processService.RunSsh(host);
        recents.Record(host);
    }
}
