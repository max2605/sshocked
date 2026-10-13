using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Sshocked.Tests;

internal sealed class ExternalBoundedTestChild : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-bounded-tests-" + Guid.NewGuid().ToString("N"));
    private string PidPath => Path.Combine(directory, "pid");
    private string ReleasePath => Path.Combine(directory, "release");

    public ProcessStartInfo Info(string windows, string unix)
    {
        Directory.CreateDirectory(directory);
        var command = OperatingSystem.IsWindows()
            ? "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); [IO.File]::WriteAllText($env:SSHOCKED_BOUND_PID,[string]$PID); while(-not [IO.File]::Exists($env:SSHOCKED_BOUND_RELEASE)){Start-Sleep -Milliseconds 20}; " + windows
            : "set -eu\nprintf '%s' $$ > \"$SSHOCKED_BOUND_PID\"; while [ ! -f \"$SSHOCKED_BOUND_RELEASE\" ]; do sleep 0.02; done; " + unix;
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : LocalShellTestRunner.ShellPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command" }) info.ArgumentList.Add(argument);
        else info.ArgumentList.Add("-c");
        info.ArgumentList.Add(command);
        info.Environment["SSHOCKED_BOUND_PID"] = PidPath;
        info.Environment["SSHOCKED_BOUND_RELEASE"] = ReleasePath;
        return info;
    }

    public async Task<Process> Ready(Task operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (operation.IsCompleted) await operation;
            string? text = null;
            try { if (File.Exists(PidPath)) text = await File.ReadAllTextAsync(PidPath, timeout.Token); }
            catch (IOException) { }
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) return Process.GetProcessById(pid);
            await Task.Delay(20, timeout.Token);
        }
    }

    public void Release() => File.WriteAllText(ReleasePath, "ready");

    public void Dispose()
    {
        var path = Path.GetFullPath(directory);
        var intended = Path.GetFullPath(Path.GetTempPath()) + "sshocked-bounded-tests-";
        if (!path.StartsWith(intended, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected bounded-test cleanup path.");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
