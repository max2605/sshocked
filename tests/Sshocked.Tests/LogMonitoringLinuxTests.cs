using System.Diagnostics;
using System.Text;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class LogMonitoringLinuxTests
{
    [Fact]
    public async Task RealDescriptorReadResumesAndDetectsCopytruncateFastRegrowthAndRename()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "ssk-log-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "app'; echo sentinel; #.log");
        try
        {
            const string original = "Grüße ERROR\nsecond\n";
            await File.WriteAllTextAsync(path, original);
            var first = await Read(path);
            Assert.Equal(Encoding.UTF8.GetBytes(original), first.Bytes);
            Assert.Equal("initial", first.Reset);
            var cursor = Cursor(first);
            await File.AppendAllTextAsync(path, "next\n");
            var appended = await Read(path, cursor);
            Assert.Equal("none", appended.Reset);
            Assert.Equal(Encoding.UTF8.GetBytes("next\n"), appended.Bytes);
            cursor = Cursor(appended);
            await File.WriteAllTextAsync(path, string.Concat(Enumerable.Repeat("different generation\n", 20)));
            var truncated = await Read(path, cursor);
            Assert.Equal(first.Identity, truncated.Identity);
            Assert.Equal("anchor", truncated.Reset);
            Assert.Equal(0, truncated.Start);
            cursor = Cursor(truncated);
            File.Move(path, path + ".1");
            await File.WriteAllTextAsync(path, "replacement\n");
            var renamed = await Read(path, cursor);
            Assert.Equal("identity", renamed.Reset);
            Assert.NotEqual(cursor.Identity, renamed.Identity);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AReadFailureInTheHashPipelineCannotBecomeSuccessfulEmptyData()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "ssk-log-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "app.log");
        var fakeDd = Path.Combine(directory, "dd");
        try
        {
            await File.WriteAllTextAsync(path, "secret\n");
            await File.WriteAllTextAsync(fakeDd, "#!/bin/sh\nexit 77\n");
            File.SetUnixFileMode(fakeDd, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var info = Info(path, null);
            info.Environment["PATH"] = directory + ":" + Environment.GetEnvironmentVariable("PATH");
            var result = await new ExternalProcessRunner().RunBounded(info, 65536);
            Assert.NotEqual(0, result.ExitCode);
            Assert.DoesNotContain("SSKLOG1", result.StandardOutput);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<LogMonitoringRead> Read(string path, LogMonitoringCursor? cursor = null)
    {
        var result = await new ExternalProcessRunner().RunBounded(Info(path, cursor), 65536);
        Assert.Equal(0, result.ExitCode);
        return LogMonitoringParser.Parse(result.StandardOutput, 32768);
    }

    private static LogMonitoringCursor Cursor(LogMonitoringRead read) => new(read.Identity, read.Start + read.Bytes.Length,
        read.PrefixLength, read.PrefixHash, read.AnchorOffset, read.AnchorLength, read.AnchorHash, false, 1);

    private static ProcessStartInfo Info(string path, LogMonitoringCursor? cursor)
    {
        var info = new ProcessStartInfo("bash") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(LogMonitoringCommands.Read(path, cursor, false, 32768));
        return info;
    }
}
