using System.Diagnostics;

namespace Sshocked.Services;

public static class AzureCliCommand
{
    private const string Executable = "az";
    private const string WindowsExecutable = "az.exe";
    private const string WindowsLauncher = "az.cmd";
    private const string PythonExecutable = "python.exe";
    private const string PythonModuleFlag = "-IBm";
    private const string PythonModule = "azure.cli";
    private const string PathEnvironment = "PATH";
    private const string UnbufferedEnvironment = "PYTHONUNBUFFERED";

    public static ProcessStartInfo Create(IEnumerable<string> arguments, bool captureOutput = true)
    {
        var info = new ProcessStartInfo { FileName = Executable, UseShellExecute = false, RedirectStandardOutput = captureOutput, RedirectStandardError = captureOutput, CreateNoWindow = captureOutput };
        if (OperatingSystem.IsWindows())
        {
            // Azure CLI's Windows installer ships az.cmd. Invoke its bundled Python
            // directly so untrusted resource names never pass through cmd.exe.
            foreach (var directory in (Environment.GetEnvironmentVariable(PathEnvironment) ?? "").Split(Path.PathSeparator))
            {
                var path = directory.Trim().Trim('"');
                if (path.Length == 0) continue;
                var executable = Path.Combine(path, WindowsExecutable);
                if (File.Exists(executable)) { info.FileName = executable; break; }
                if (!File.Exists(Path.Combine(path, WindowsLauncher))) continue;
                var python = Path.GetFullPath(Path.Combine(path, "..", PythonExecutable));
                if (!File.Exists(python)) continue;
                info.FileName = python;
                info.ArgumentList.Add(PythonModuleFlag);
                info.ArgumentList.Add(PythonModule);
                break;
            }
        }
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment[UnbufferedEnvironment] = "1";
        return info;
    }
}
