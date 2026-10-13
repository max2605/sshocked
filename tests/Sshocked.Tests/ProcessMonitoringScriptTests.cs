using System.Diagnostics;
using System.Text;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ProcessMonitoringScriptTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedPosixScriptsParseWithoutRunningProbes(bool service)
    {
        if (!File.Exists(LocalShellTestRunner.ShellPath)) return;
        var fake = new ProcessMonitoringTestTransport { Result = new(0, service ? "ActiveState=active\n" : "META\tboot\t1\t100\t2\t1\n", "") };
        var definition = new MonitorDefinition { Name = "Syntax", Options = new() { ["name"] = "test'quote", ["platform"] = "linux" } };
        if (service)
        {
            definition.Options["needrestart"] = "true";
            await new ServiceMonitoringProvider(fake).Collect(new(), definition, null);
        }
        else await new ProcessMonitoringProvider(fake).Collect(new(), definition, null);
        var script = SshArguments.Tokenize(fake.Command)[2];
        var info = LocalShellTestRunner.ShellInfo();
        info.ArgumentList.Add("-n");
        var result = await new ExternalProcessRunner().RunWithInput(info, script + "\n");
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedWindowsScriptsParseWithoutRunningCim(bool service)
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable)) return;
        var fake = new ProcessMonitoringTestTransport { Result = new(0, service ? "{\"state\":\"Running\"}" : "{\"complete\":true,\"processes\":[]}", "") };
        var definition = new MonitorDefinition { Name = "Syntax", Options = new() { ["name"] = "test';quote", ["platform"] = "windows" } };
        if (service) await new ServiceMonitoringProvider(fake).Collect(new(), definition, null);
        else await new ProcessMonitoringProvider(fake).Collect(new(), definition, null);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(fake.Command.Split(' ').Last()));
        var parser = "$tokens=$null;$errors=$null;[void][System.Management.Automation.Language.Parser]::ParseInput(" +
            ProcessMonitorParsing.PowerShellQuote(script) + ",[ref]$tokens,[ref]$errors);if($errors.Count){$errors | ForEach-Object {Write-Error $_.Message};exit 1}";
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(parser)));
        var result = await new ExternalProcessRunner().Run(info);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }
}
