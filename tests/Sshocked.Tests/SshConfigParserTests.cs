using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SshConfigParserTests
{
    private readonly SshConfigImporter _importer = new(NullLogger<SshConfigImporter>.Instance);

    [Fact]
    public void ImportsConcreteAliasesWithKeysProxyAndCustomOptionsAndWildcardDefaults()
    {
        var hosts = _importer.Parse("""
            # sshocked-id: stable-id
            Host web web-short
                HostName=10.0.0.5
                IdentityFile "~/.ssh/key with spaces" # preserve the path
                ProxyJump admin@bastion:2222
                ServerAliveInterval 30
            Host * !excluded
                User fallback
                Port 2200
                StrictHostKeyChecking accept-new
            Host excluded
                HostName excluded.example
            """);
        Assert.Equal(3, hosts.Count);
        var web = hosts.Single(h => h.Alias == "web");
        Assert.Equal("stable-id", web.Id);
        Assert.Equal("10.0.0.5", web.HostName);
        Assert.Equal("fallback", web.User);
        Assert.Equal(2200, web.Port);
        Assert.Equal("~/.ssh/key with spaces", web.IdentityFile);
        Assert.Equal(AuthType.SshKey, web.AuthType);
        Assert.Equal("admin@bastion:2222", web.ProxyJump);
        Assert.Contains("serveraliveinterval=30", web.CustomSshOptions);
        Assert.Contains("stricthostkeychecking=accept-new", web.CustomSshOptions);
        Assert.Equal(22, hosts.Single(h => h.Alias == "excluded").Port);
        Assert.Equal(3, hosts.Select(h => h.Id).Distinct().Count());
    }

    [Fact]
    public void OpenSshFirstObtainedValueWinsAndMatchBlocksDoNotLeakIntoHosts()
    {
        var hosts = _importer.Parse("""
            User global
            Host one
                HostName one.example
                User ignored
                Port 2222
                Port 3333
            Match exec "something"
                HostName conditional.example
                Port 4444
            Host two
                HostName two.example
            """);
        Assert.Equal("global", hosts[0].User);
        Assert.Equal("one.example", hosts[0].HostName);
        Assert.Equal(2222, hosts[0].Port);
        Assert.Equal(22, hosts[1].Port);
    }

    [Fact]
    public void ImportExpandsQuotedIncludeFilesAndRejectsCycles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-includes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var main = Path.Combine(directory, "config");
            var included = Path.Combine(directory, "extra config");
            File.WriteAllText(main, "Include \"extra config\"\n");
            File.WriteAllText(included, "Host imported\n HostName imported.example\n");
            Assert.Equal("imported", Assert.Single(_importer.Import(main)).Alias);
            File.WriteAllText(included, "Include config\n");
            Assert.Throws<FormatException>(() => _importer.Import(main));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void InvalidPortFailsWholePreviewInsteadOfPartiallyApplying()
        => Assert.Throws<FormatException>(() => _importer.Parse("Host invalid\n Port 70000\n"));
}
