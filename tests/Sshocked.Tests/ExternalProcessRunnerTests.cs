using System.Diagnostics;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ExternalProcessRunnerTests
{
    [Fact]
    public async Task CapturedStdoutAndStderrDrainConcurrentlyWithoutDeadlock()
    {
        var info = Shell(OperatingSystem.IsWindows()
            ? "1..10000 | ForEach-Object { [Console]::WriteLine('output'); [Console]::Error.WriteLine('error') }; exit 19"
            : "i=0; while [ $i -lt 10000 ]; do echo output; echo error >&2; i=$((i+1)); done; exit 19");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await new ExternalProcessRunner().RunAsync(info, timeout.Token);
        Assert.Equal(19, result.ExitCode);
        Assert.Equal(10000, result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(10000, result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task CancellationStopsTheChildProcessPromptly()
    {
        var info = Shell(OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 60" : "sleep 60");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ExternalProcessRunner().RunAsync(info, cancellation.Token));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
    }

    private static ProcessStartInfo Shell(string command)
    {
        var info = new ProcessStartInfo { FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (OperatingSystem.IsWindows()) { info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command"); }
        else info.ArgumentList.Add("-c");
        info.ArgumentList.Add(command);
        return info;
    }
}
