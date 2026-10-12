using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SshConfigSyncTestsFakeExporter : ISshConfigExporter
{
    public int Calls { get; private set; }

    public SshConfigExportResult Export(IEnumerable<ServerHost> hosts, SshConfigSyncSettings settings, string? configPath = null)
    {
        Calls++;
        return new(hosts.Count(), false, [], []);
    }
}
