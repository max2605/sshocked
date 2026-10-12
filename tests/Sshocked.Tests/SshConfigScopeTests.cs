using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SshConfigScopeTests
{
    [Fact]
    public void ExportRetainsGlobalPreambleSemanticsAndResetsScopeBeforeUserEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configPath = Path.Combine(directory, "config");
            File.WriteAllText(configPath, "User global\nHost manual\n HostName manual.example\n");
            new SshConfigExporter().Export([new ServerHost { Id = "managed", Alias = "managed", HostName = "managed.example", User = "specific" }], new(), configPath);
            var imported = new SshConfigImporter(NullLogger<SshConfigImporter>.Instance).Import(configPath);
            Assert.All(imported, host => Assert.Equal("global", host.User));
            Assert.StartsWith("User global\n# SSHOCKED_BEGIN", File.ReadAllText(configPath));
            Assert.Contains("Host *\n# SSHOCKED_END\nHost manual", File.ReadAllText(configPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
