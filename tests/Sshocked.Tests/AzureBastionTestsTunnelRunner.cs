using System.Diagnostics;

using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class AzureBastionTestsTunnelRunner(bool fail) : IExternalProcessRunner
{
    public int StartedPid { get; private set; }

    public Process Start(ProcessStartInfo ignored)
    {
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(fail ? "[Console]::Error.WriteLine('target not found'); exit 7" : "Write-Output 'Tunnel is ready'; Start-Sleep -Seconds 60");
        }
        else
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(fail ? "echo 'target not found' >&2; exit 7" : "echo 'Tunnel is ready'; sleep 60");
        }

        var process = Process.Start(info)!;
        StartedPid = process.Id;
        return process;
    }

    public Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        var args = startInfo.ArgumentList;
        var result = args.Contains("version") ? new ProcessResult(0, "{\"azure-cli\":\"2.62.0\"}", "") : args.Contains("extension") ? new ProcessResult(0, "[{\"name\":\"bastion\"}]", "") : new ProcessResult(0, "{}", "");
        return Task.FromResult(result);
    }
}
