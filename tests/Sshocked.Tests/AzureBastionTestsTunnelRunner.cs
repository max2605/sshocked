using System.Diagnostics;

using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class AzureBastionTestsTunnelRunner(bool fail, string failureOutput = "target not found", bool failureToStandardOutput = false, int preludeLength = 0, bool failAfterReady = false) : IExternalProcessRunner
{
    private Process? startedProcess;
    public int StartedPid { get; private set; }

    public Process Start(ProcessStartInfo ignored)
    {
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = failAfterReady,
            CreateNoWindow = true
        };
        info.Environment["SSHOCKED_TEST_BASTION_FAILURE"] = failureOutput;
        info.Environment["SSHOCKED_TEST_BASTION_PRELUDE_LENGTH"] = preludeLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            var stream = failureToStandardOutput ? "Out" : "Error";
            var failureCommand = $"if ([int]$env:SSHOCKED_TEST_BASTION_PRELUDE_LENGTH -gt 0) {{ [Console]::{stream}.WriteLine(('x' * [int]$env:SSHOCKED_TEST_BASTION_PRELUDE_LENGTH)) }}; [Console]::{stream}.WriteLine($env:SSHOCKED_TEST_BASTION_FAILURE); exit 7";
            info.ArgumentList.Add(failAfterReady ? "Write-Output 'Tunnel is ready'; [Console]::ReadLine() | Out-Null; " + failureCommand
                : fail ? failureCommand : "Write-Output 'Tunnel is ready'; Start-Sleep -Seconds 60");
        }
        else
        {
            info.ArgumentList.Add("-c");
            var redirect = failureToStandardOutput ? "" : " >&2";
            var failureCommand = "(if [ \"$SSHOCKED_TEST_BASTION_PRELUDE_LENGTH\" -gt 0 ]; then printf '%*s\\n' \"$SSHOCKED_TEST_BASTION_PRELUDE_LENGTH\" '' | tr ' ' x; fi; printf '%s\\n' \"$SSHOCKED_TEST_BASTION_FAILURE\")" + redirect + "; exit 7";
            info.ArgumentList.Add(failAfterReady ? "echo 'Tunnel is ready'; read -r release; " + failureCommand
                : fail ? failureCommand : "echo 'Tunnel is ready'; sleep 60");
        }

        var process = Process.Start(info)!;
        startedProcess = process;
        StartedPid = process.Id;
        return process;
    }

    public async Task ReleaseFailure()
    {
        await startedProcess!.StandardInput.WriteLineAsync("exit");
        await startedProcess.StandardInput.FlushAsync();
        await startedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    public Task<ProcessResult> Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        var args = startInfo.ArgumentList;
        var result = args.Contains("version") ? new ProcessResult(0, "{\"azure-cli\":\"2.62.0\"}", "") : args.Contains("extension") ? new ProcessResult(0, "[{\"name\":\"bastion\"}]", "") : new ProcessResult(0, "{}", "");
        return Task.FromResult(result);
    }
}
