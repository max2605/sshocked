using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SshAskPassTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("dotnet")]
    [InlineData("dotnet.exe")]
    [InlineData("DOTNET.EXE")]
    [InlineData("/usr/share/dotnet/dotnet")]
    public void InvalidExecutableDoesNotWritePasswordEnvironment(string? executable)
    {
        var variables = new Dictionary<string, string?> { ["EXISTING"] = "unchanged" };
        Assert.Throws<InvalidOperationException>(() => SshAskPass.Configure(variables, "private-login-value", executable));
        Assert.Equal(new Dictionary<string, string?> { ["EXISTING"] = "unchanged" }, variables);
    }

    [Fact]
    public void ApphostPathSupportsSpacesAndKeepsPasswordInTheChildEnvironment()
    {
        var executable = Path.Combine(Path.GetTempPath(), "sshocked application", "ssk");
        var variables = new Dictionary<string, string?>();
        var inheritedPassword = Environment.GetEnvironmentVariable(SshAskPass.PasswordEnvironment);

        SshAskPass.Configure(variables, "private-login-value", executable);

        Assert.Equal(executable, variables["SSH_ASKPASS"]);
        Assert.Equal("force", variables["SSH_ASKPASS_REQUIRE"]);
        Assert.Equal("1", variables[SshAskPass.HelperEnvironment]);
        Assert.Equal("private-login-value", variables[SshAskPass.PasswordEnvironment]);
        Assert.Equal(inheritedPassword, Environment.GetEnvironmentVariable(SshAskPass.PasswordEnvironment));
    }
}
