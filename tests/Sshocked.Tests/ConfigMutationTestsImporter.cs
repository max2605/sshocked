using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class ConfigMutationTestsImporter : ISshConfigImporter
{
    public List<ServerHost> Import(string? configPath = null) => [];
    public List<ServerHost> Parse(string configContent) => [];
}
