using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISshConfigImporter
{
    List<ServerHost> Import(string? configPath = null);
    List<ServerHost> Parse(string configContent);
}
