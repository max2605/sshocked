using System.Diagnostics;

namespace Sshocked.Services;

public static class AzureCliCommand
{
    public static ProcessStartInfo Create(IEnumerable<string> arguments, bool captureOutput = true)
    {
        var info = new ProcessStartInfo { FileName = "az", UseShellExecute = false, RedirectStandardOutput = captureOutput, RedirectStandardError = captureOutput, CreateNoWindow = captureOutput };
        if (OperatingSystem.IsWindows())
        {
            // Azure CLI's Windows installer ships az.cmd. Invoke its bundled Python
            // directly so untrusted resource names never pass through cmd.exe.
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                var path = directory.Trim().Trim('"');
                if (path.Length == 0) continue;
                var executable = Path.Combine(path, "az.exe");
                if (File.Exists(executable)) { info.FileName = executable; break; }
                if (!File.Exists(Path.Combine(path, "az.cmd"))) continue;
                var python = Path.GetFullPath(Path.Combine(path, "..", "python.exe"));
                if (!File.Exists(python)) continue;
                info.FileName = python;
                info.ArgumentList.Add("-IBm");
                info.ArgumentList.Add("azure.cli");
                break;
            }
        }
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["PYTHONUNBUFFERED"] = "1";
        return info;
    }
}
