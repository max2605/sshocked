using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SshConfigSyncTestsFakeImporter : ISshConfigImporter
{
    public int Calls { get; private set; }

    public List<ServerHost> Import(string? configPath = null)
    {
        Calls++;
        return [];
    }

    public List<ServerHost> Parse(string configContent) => [];
}
