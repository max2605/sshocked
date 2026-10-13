using System.Diagnostics;
using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

internal sealed class LocalShellTestRunner : IExternalProcessRunner
{
    private readonly ExternalProcessRunner runner = new();
    public Process? LastProcess { get; private set; }
    public static string ShellPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "sh.exe") : "/bin/sh";

    public Process Start(ProcessStartInfo startInfo)
    {
        var local = ShellInfo();
        local.RedirectStandardInput = true;
        local.StandardInputEncoding = new UTF8Encoding(false);
        local.ArgumentList.Add("-s");
        return LastProcess = runner.Start(local);
    }

    public Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default) => runner.Run(startInfo, cancellationToken);

    public static ProcessStartInfo ShellInfo(string? command = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = ShellPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (OperatingSystem.IsWindows()) info.Environment["PATH"] = Path.GetDirectoryName(ShellPath) + ";" + Environment.GetEnvironmentVariable("PATH");
        if (command is not null) { info.ArgumentList.Add("-c"); info.ArgumentList.Add(command); }
        return info;
    }
}
