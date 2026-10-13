using Microsoft.Extensions.Logging.Abstractions;

using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class FileTransferTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-transfers-" + Guid.NewGuid().ToString("N"));
    private static ServerHost Host(string alias = "web") => new() { Id = alias, Alias = alias, User = "alice", HostName = "server.example", Port = 2222 };

    public FileTransferTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void ScpUsesUppercasePortAndLiteralPathsWithSpacesAndShellPunctuation()
    {
        var host = Host();
        host.IdentityFile = "C:/keys/key with spaces";
        var local = Path.Combine(directory, "data & report.txt");
        var args = ScpArguments.Build(host, local, "/tmp/a b; touch nope.txt", true);
        Assert.Equal(new[] { "-P", "2222", "-i", host.IdentityFile, "--", local, "alice@server.example:/tmp/a b; touch nope.txt" }, args);
        Assert.DoesNotContain("-p", args);
        Assert.Equal(args[^1], ScpArguments.Build(host, local, "/tmp/a b; touch nope.txt", false)[^2]);
    }

    [Fact]
    public void ScpUsesIpv6BracketsAndBastionHostKeyAlias()
    {
        var host = Host();
        host.HostName = "2001:db8::123";
        host.SshHostKeyAlias = "vm-resource-id";
        var args = ScpArguments.Build(host, Path.Combine(directory, "file"), "~/file", true, true);
        Assert.Contains("-r", args);
        Assert.Contains("HostKeyAlias=vm-resource-id", args);
        Assert.Equal("alice@[2001:db8::123]:~/file", args[^1]);
    }

    [Theory]
    [InlineData("-v")]
    [InlineData("-vv")]
    [InlineData("-vvv")]
    public void ScpParsesCustomOptionsWithoutExecutingShell(string verbosity)
    {
        var host = Host();
        host.AuthType = AuthType.CustomConfig;
        host.CustomSshOptions = "-o 'ProxyCommand=ssh -W %h:%p jump' -F \"config with spaces\" -C -4 " + verbosity;
        var args = ScpArguments.Build(host, Path.Combine(directory, "file"), "/tmp/file", true);
        Assert.Contains("ProxyCommand=ssh -W %h:%p jump", args);
        Assert.Contains("config with spaces", args);
        Assert.Contains("-C", args);
        Assert.Contains(verbosity, args);
    }

    [Theory]
    [InlineData("-O")]
    [InlineData("-t")]
    [InlineData("-N")]
    [InlineData("-p 23")]
    [InlineData("-o")]
    [InlineData("destination")]
    public void ScpRejectsUnsupportedOrIncompleteOptions(string options)
    {
        var host = Host();
        host.AuthType = AuthType.CustomConfig;
        host.CustomSshOptions = options;
        Assert.Throws<ArgumentException>(() => ScpArguments.Build(host, Path.Combine(directory, "file"), "/tmp/file", true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    public void ScpRejectsInvalidPaths(string path) => Assert.Throws<ArgumentException>(() =>
        ScpArguments.Build(Host(), Path.Combine(directory, "file"), path, true));

    [Fact]
    public void GroupPathsHandleRepeatedAliasesExistingFilesAndTraversalCharacters()
    {
        File.WriteAllText(Path.Combine(directory, "report-same.txt"), "keep existing file");
        var hosts = new[] { Host("same"), Host("same"), Host("../../outside\\bad:alias") };
        var paths = FileTransferService.BuildGroupDownloadPaths(hosts, "/var/report.txt", directory);
        Assert.Equal("report-same-2.txt", Path.GetFileName(paths[0].LocalPath));
        Assert.Equal("report-same-3.txt", Path.GetFileName(paths[1].LocalPath));
        Assert.Equal(3, paths.Select(path => path.LocalPath).Distinct().Count());
        Assert.All(paths, path => Assert.Equal(directory, Path.GetDirectoryName(path.LocalPath)));
        Assert.Equal("keep existing file", File.ReadAllText(Path.Combine(directory, "report-same.txt")));
    }

    [Fact]
    public void DirectoryParserKeepsSpacesAndMetadataAndOmitsDotEntries()
    {
        const string listing = "total 12\n" +
            "drwxr-xr-x 2 1000 1000 4096 Oct 12 12:10 .\n" +
            "drwxr-xr-x 2 1000 1000 4096 Oct 12 12:10 ..\n" +
            "-rw-r--r-- 1 1000 1000 123 Jan  2 2025 report with spaces.txt\n" +
            "drwxr-xr-x 2 1000 1000 4096 Jan  3 2025 folder name\n" +
            "lrwxrwxrwx 1 1000 1000 12 Jan  4 2025 symbolic -> target\n";
        var entries = FileTransferService.ParseDirectoryListing(listing, "/var/log");
        Assert.Equal(3, entries.Count);
        Assert.True(entries[0].IsDirectory);
        var report = entries.Single(entry => entry.Name == "report with spaces.txt");
        Assert.Equal("/var/log/report with spaces.txt", report.FullPath);
        Assert.Equal(123, report.Size);
        Assert.Equal(new DateTime(2025, 1, 2), report.LastModified);
        Assert.Equal("symbolic", entries.Single(entry => entry.Permissions.StartsWith('l')).Name);
    }

    [Fact]
    public async Task TransferInheritsConsolePropagatesLeaseEnvironmentAndDisposesLease()
    {
        var runner = new FileTransferTestsRunner();
        var processes = new FileTransferTestsProcesses();
        var audit = new FileTransferTestsAudit();
        var service = new FileTransferService(processes, runner, audit, NullLogger<FileTransferService>.Instance);
        var local = Path.Combine(directory, "file.txt");
        File.WriteAllText(local, "test");
        Assert.Equal(0, await service.UploadFile(Host(), local, "~/file.txt"));
        Assert.False(runner.Calls[0].RedirectStandardOutput);
        Assert.False(runner.Calls[0].UseShellExecute);
        Assert.Equal("lease-askpass", runner.Calls[0].Environment["SSH_ASKPASS"]);
        Assert.True(processes.Leases[0].Disposed);
        Assert.True(Assert.Single(audit.Entries).Success);
    }

    [Fact]
    public async Task GroupContinuesAfterFailedHostAndReportsPartialSuccess()
    {
        var runner = new FileTransferTestsRunner();
        runner.Handler = _ => Task.FromResult(new ProcessResult(runner.Calls.Count == 1 ? 1 : 0, "", ""));
        var audit = new FileTransferTestsAudit();
        var service = new FileTransferService(new FileTransferTestsProcesses(), runner, audit, NullLogger<FileTransferService>.Instance);
        var local = Path.Combine(directory, "file.txt");
        File.WriteAllText(local, "test");
        var results = await service.UploadToGroup(new[] { Host("one"), Host("two") }, local, "/tmp/file.txt");
        Assert.False(results[0].Success);
        Assert.True(results[1].Success);
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact]
    public async Task MissingLocalFileFailsBeforeStartingAnyProcess()
    {
        var runner = new FileTransferTestsRunner();
        var service = new FileTransferService(new FileTransferTestsProcesses(), runner, new FileTransferTestsAudit(), NullLogger<FileTransferService>.Instance);
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.UploadFile(Host(), Path.Combine(directory, "missing"), "~"));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task BrowseFailsOnExitCodeInsteadOfTreatingStderrAsFiles()
    {
        var runner = new FileTransferTestsRunner { Handler = _ => Task.FromResult(new ProcessResult(1, "", "permission denied")) };
        var service = new FileTransferService(new FileTransferTestsProcesses(), runner, new FileTransferTestsAudit(), NullLogger<FileTransferService>.Instance);
        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(() => service.ListRemoteDirectory(Host(), "~/a'b; touch nope"));
        Assert.Contains("permission denied", exception.Message);
        Assert.Contains("'\"'\"'", runner.Calls[0].ArgumentList[^1]);
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/var/log", "/var")]
    [InlineData("/var", "/")]
    [InlineData("~/logs", "~")]
    public void RemoteNavigationHandlesRootAndHome(string path, string parent) => Assert.Equal(parent, FileTransferUi.ParentPath(path));

    public void Dispose() => Directory.Delete(directory, true);
}
