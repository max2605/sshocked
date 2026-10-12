using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class TerminalEnvironmentTests
{
    [Fact]
    public void MacTerminalShellCarriesConfigDirectoryEvenWhenTerminalAlreadyRuns()
    {
        var command = TerminalLauncherService.BuildMacShellCommand("/Applications/ssk app/ssk", ["--host-id", "host-id"], "/Users/name/config's folder");
        Assert.StartsWith("'env' 'SSHOCKED_CONFIG_DIR=/Users/name/config'\\''s folder' ", command);
        Assert.Contains("'/Applications/ssk app/ssk' '--host-id' 'host-id'", command);
    }
}
