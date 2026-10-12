using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Sshocked.Models;

namespace Sshocked.Services;

public static class HookLocalProcess
{
    public static ProcessStartInfo BuildLocalStartInfo(HookScript hook, IReadOnlyDictionary<string, string> environment,
        out string? temporaryScript)
    {
        temporaryScript = null;
        string executable;
        var arguments = new List<string>();
        if (hook.Mode == HookExecutionMode.ShellCommand)
        {
            executable = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh";
            if (OperatingSystem.IsWindows()) arguments.AddRange(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command"]);
            else arguments.Add("-c");
            arguments.Add((hook.Command ?? string.Empty) + (string.IsNullOrWhiteSpace(hook.Arguments) ? "" : " " + hook.Arguments));
        }
        else if (hook.Mode is HookExecutionMode.LocalScript or HookExecutionMode.InlineScript)
        {
            var script = hook.ScriptPath;
            if (hook.Mode == HookExecutionMode.InlineScript)
            {
                temporaryScript = Path.Combine(Path.GetTempPath(), $"sshocked-hook-{Guid.NewGuid():N}" + (OperatingSystem.IsWindows() ? ".ps1" : ".sh"));
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(temporaryScript, options))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(hook.InlineScript);
                script = temporaryScript;
            }
            script = Path.GetFullPath(Environment.ExpandEnvironmentVariables(script ?? string.Empty));
            if (!File.Exists(script)) throw new FileNotFoundException("The configured hook script does not exist.", script);
            var extension = Path.GetExtension(script).ToLowerInvariant();
            switch (extension)
            {
                case ".ps1":
                    executable = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
                    arguments.AddRange(["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script]);
                    break;
                case ".sh":
                    executable = OperatingSystem.IsWindows() ? "bash" : "/bin/sh";
                    arguments.Add(script);
                    break;
                case ".py":
                    executable = OperatingSystem.IsWindows() ? "python" : "python3";
                    arguments.Add(script);
                    break;
                default:
                    executable = script;
                    break;
            }
            foreach (var argument in SshArguments.Tokenize(hook.Arguments ?? string.Empty))
                arguments.Add(Regex.Replace(argument, HookConstants.EnvironmentPlaceholderPattern, match =>
                    environment.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value));
        }
        else throw new ArgumentException("Remote commands must run inside the SSH session wrapper.");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach (var variable in environment) startInfo.Environment[variable.Key] = variable.Value;
        return startInfo;
    }

}
