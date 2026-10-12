using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SshConfigExporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sshocked-export-" + Guid.NewGuid().ToString("N"));
    private readonly SshConfigExporter _exporter = new();
    public SshConfigExporterTests() => Directory.CreateDirectory(_directory);
    private string ConfigPath => Path.Combine(_directory, "config");

    [Fact]
    public void MarkedExportPreservesUnmanagedCommentsDefaultsAndCreatesBackupThenIsIdempotent()
    {
        const string original = "# my comment\r\nHost manual\r\n HostName manual.example\r\nHost *\r\n User default\r\n";
        File.WriteAllText(ConfigPath, original);
        var result = _exporter.Export([Host("web")], new(), ConfigPath);
        Assert.True(result.Changed);
        Assert.Equal(original, File.ReadAllText(Assert.Single(result.Backups)));
        Assert.Equal(original, SshConfigExporter.ReplaceManagedBlock(File.ReadAllText(ConfigPath), ""));
        Assert.Contains("\r\n", File.ReadAllText(ConfigPath));
        var repeated = _exporter.Export([Host("web")], new(), ConfigPath);
        Assert.False(repeated.Changed);
        Assert.Empty(repeated.Backups);
        _exporter.Export([Host("replacement")], new(), ConfigPath);
        Assert.DoesNotContain("Host web", File.ReadAllText(ConfigPath));
        Assert.Equal(original, SshConfigExporter.ReplaceManagedBlock(File.ReadAllText(ConfigPath), ""));
    }

    [Fact]
    public void IncludeExportIsIdempotentAndRefusesUnmanagedIncludeDestination()
    {
        var includePath = Path.Combine(_directory, "generated keys");
        var settings = new SshConfigSyncSettings
        {
            Strategy = ExportStrategy.IncludeFile,
            IncludeFilePath = includePath
        };
        File.WriteAllText(ConfigPath, "# main\nHost native\n HostName native.example\n");
        _exporter.Export([Host("web")], settings, ConfigPath);
        Assert.Contains("Include ", File.ReadAllText(ConfigPath));
        Assert.False(_exporter.Export([Host("web")], settings, ConfigPath).Changed);
        var imported = new SshConfigImporter(NullLogger<SshConfigImporter>.Instance).Import(ConfigPath);
        Assert.Equal(2, imported.Count);
        File.WriteAllText(includePath, "Host user\n HostName important.example\n");
        var mainBefore = File.ReadAllText(ConfigPath);
        Assert.Throws<InvalidOperationException>(() => _exporter.Export([Host("replacement")], settings, ConfigPath));
        Assert.Equal(mainBefore, File.ReadAllText(ConfigPath));
        Assert.Contains("important.example", File.ReadAllText(includePath));
    }

    [Theory]
    [InlineData("# SSHOCKED_BEGIN\nHost broken\n")]
    [InlineData("# SSHOCKED_END\n")]
    [InlineData("# SSHOCKED_BEGIN\n# SSHOCKED_END\n# SSHOCKED_BEGIN\n# SSHOCKED_END\n")]
    public void MalformedMarkersNeverChangeExistingFile(string original)
    {
        File.WriteAllText(ConfigPath, original);
        Assert.Throws<InvalidOperationException>(() => _exporter.Export([Host("web")], new(), ConfigPath));
        Assert.Equal(original, File.ReadAllText(ConfigPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.bak"));
    }

    [Fact]
    public void ExportFiltersByTagsAndExclusionsAndRejectsDirectiveInjection()
    {
        var web = Host("web");
        web.Tags = ["production"];
        var excluded = Host("excluded");
        excluded.Tags = ["production"];
        var dev = Host("dev");
        dev.Tags = ["dev"];
        var settings = new SshConfigSyncSettings
        {
            ExportAllHosts = false,
            ExportTags = ["PRODUCTION"],
            ExcludeHostIds = [excluded.Id]
        };
        Assert.Equal(1, _exporter.Export([web, excluded, dev], settings, ConfigPath).Exported);
        var before = File.ReadAllText(ConfigPath);
        web.HostName = "safe\nHost injected";
        Assert.Throws<InvalidOperationException>(() => _exporter.Export([web], settings, ConfigPath));
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void RoundTripPreservesIdentityProxyAndCustomOptionValues()
    {
        var host = Host("roundtrip");
        host.IdentityFile = "~/.ssh/key with spaces";
        host.ProxyJump = "user@bastion:2222";
        host.CustomSshOptions = "-o \"ProxyCommand=nc -x proxy:1080 %h %p\" -o ServerAliveInterval=60";
        _exporter.Export([host], new(), ConfigPath);
        var imported = Assert.Single(new SshConfigImporter(NullLogger<SshConfigImporter>.Instance).Import(ConfigPath));
        Assert.Equal(host.Id, imported.Id);
        Assert.Equal(host.IdentityFile, imported.IdentityFile);
        Assert.Equal(host.ProxyJump, imported.ProxyJump);
        Assert.Contains("proxycommand=nc -x proxy:1080 %h %p", imported.CustomSshOptions);
        Assert.Contains("serveraliveinterval=60", imported.CustomSshOptions);
        Assert.Contains("    proxycommand nc -x proxy:1080 %h %p", File.ReadAllText(ConfigPath));
    }

    [SymlinkFact]
    public void SymbolicMainConfigIsRefusedBeforeIncludeOrBackupWrites()
    {
        var target = Path.Combine(_directory, "dotfile-config");
        const string original = "# managed externally\nHost original\n HostName original.example\n";
        File.WriteAllText(target, original);
        File.CreateSymbolicLink(ConfigPath, target);
        var include = Path.Combine(_directory, "generated");
        var settings = new SshConfigSyncSettings
        {
            Strategy = ExportStrategy.IncludeFile,
            IncludeFilePath = include
        };
        var error = Assert.Throws<InvalidOperationException>(() => _exporter.Export([Host("web")], settings, ConfigPath));
        Assert.Contains("symbolic link", error.Message);
        Assert.NotNull(new FileInfo(ConfigPath).LinkTarget);
        Assert.Equal(original, File.ReadAllText(target));
        Assert.False(File.Exists(include));
        Assert.Empty(Directory.GetFiles(_directory, "*.bak"));
    }

    [SymlinkFact]
    public void BrokenSymbolicIncludeIsRefusedWithoutWritingMainConfig()
    {
        const string original = "# native config\n";
        File.WriteAllText(ConfigPath, original);
        var include = Path.Combine(_directory, "generated");
        var missingTarget = Path.Combine(_directory, "missing-target");
        File.CreateSymbolicLink(include, missingTarget);
        var settings = new SshConfigSyncSettings
        {
            Strategy = ExportStrategy.IncludeFile,
            IncludeFilePath = include
        };
        Assert.Throws<InvalidOperationException>(() => _exporter.Export([Host("web")], settings, ConfigPath));
        Assert.NotNull(new FileInfo(include).LinkTarget);
        Assert.False(File.Exists(missingTarget));
        Assert.Equal(original, File.ReadAllText(ConfigPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.bak"));
    }

    private static ServerHost Host(string alias) => new()
    {
        Id = alias + "-id",
        Alias = alias,
        HostName = alias + ".example",
        User = "admin",
        Port = 2222
    };
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
