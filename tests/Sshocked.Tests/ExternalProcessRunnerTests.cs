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
        var result = await new ExternalProcessRunner().Run(info, timeout.Token);
        Assert.Equal(19, result.ExitCode);
        Assert.Equal(10000, result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(10000, result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task CancellationStopsTheChildProcessPromptly()
    {
        var readyPath = Path.Combine(Path.GetTempPath(), "sshocked-cancel-ready-" + Guid.NewGuid().ToString("N"));
        var info = Shell(OperatingSystem.IsWindows()
            ? "[IO.File]::WriteAllText($env:SSHOCKED_TEST_READY, [string]$PID); Start-Sleep -Seconds 60"
            : "printf '%s' $$ > \"$SSHOCKED_TEST_READY\"; sleep 60");
        info.Environment["SSHOCKED_TEST_READY"] = readyPath;
        using var cancellation = new CancellationTokenSource();
        var operation = new ExternalProcessRunner().Run(info, cancellation.Token);
        try
        {
            using var startupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string pidText = "";
            while (!int.TryParse(pidText, out _))
            {
                startupDeadline.Token.ThrowIfCancellationRequested();
                if (operation.IsCompleted) await operation;
                if (File.Exists(readyPath)) pidText = await File.ReadAllTextAsync(readyPath, startupDeadline.Token);
                if (!int.TryParse(pidText, out _)) await Task.Delay(20, startupDeadline.Token);
            }
            using var child = Process.GetProcessById(int.Parse(pidText, System.Globalization.CultureInfo.InvariantCulture));
            // Cancellation now measures a live child instead of racing PowerShell startup,
            // which varies substantially when the full suite runs alongside AOT builds.
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.True(child.HasExited);
        }
        finally
        {
            cancellation.Cancel();
            try { await operation; } catch (OperationCanceledException) { }
            if (File.Exists(readyPath)) File.Delete(readyPath);
        }
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
