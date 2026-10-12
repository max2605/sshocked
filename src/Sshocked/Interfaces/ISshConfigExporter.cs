using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISshConfigExporter
{
    SshConfigExportResult Export(IEnumerable<ServerHost> hosts, SshConfigSyncSettings settings, string? configPath = null);
}
