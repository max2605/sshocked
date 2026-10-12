using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

/// <summary>Child application sessions own tunnels, temporary credentials, hooks and history.</summary>
public sealed class TerminalLauncherService(ILogger<TerminalLauncherService> logger) : ITerminalLauncherService
{
    public Task Launch(ServerHost host, ContainerModel? container = null)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the application executable.");
        var arguments = new List<string>();
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments.Add(Path.Combine(AppContext.BaseDirectory, "ssk.dll"));
        arguments.Add("--host-id"); arguments.Add(host.Id);
        if (container is not null) { arguments.Add("--container-id"); arguments.Add(container.Id ?? container.ContainerId); }
        var info = BuildStartInfo(executable, arguments, host.Alias);
        info.Environment["SSHOCKED_CONFIG_DIR"] = ConfigPaths.DirectoryPath;
        logger.LogInformation("Launching terminal session for {Alias}", host.Alias);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start terminal.");
        return Task.CompletedTask;
    }

    public static ProcessStartInfo BuildStartInfo(string executable, IReadOnlyList<string> arguments, string title)
    {
        var info = new ProcessStartInfo { UseShellExecute = false };
        var tokens = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            info.FileName = "wt.exe";
            tokens.AddRange(["-w", "0", "new-tab", "--title", title, executable]);
            tokens.AddRange(arguments);
        }
        else if (OperatingSystem.IsMacOS())
        {
            info.FileName = "osascript";
            var command = string.Join(' ', new[] { executable }.Concat(arguments).Select(ShellQuote));
            var literal = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
            tokens.AddRange(["-e", $"tell application \"Terminal\" to do script \"{literal}\"", "-e", "tell application \"Terminal\" to activate"]);
        }
        else
        {
            var candidates = new[] { ("x-terminal-emulator", new[] { "-e" }), ("gnome-terminal", new[] { "--" }),
                ("konsole", new[] { "--new-tab", "-e" }), ("xterm", new[] { "-e" }) };
            var selected = candidates.FirstOrDefault(c => IsOnPath(c.Item1));
            if (selected.Item1 is null) throw new InvalidOperationException("No supported terminal found. Select sequential connections in Settings.");
            info.FileName = selected.Item1;
            tokens.AddRange(selected.Item2); tokens.Add(executable); tokens.AddRange(arguments);
        }
        foreach (var token in tokens) info.ArgumentList.Add(token);
        return info;
    }

    private static bool IsOnPath(string command) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Any(path => File.Exists(Path.Combine(path, command)));
    private static string ShellQuote(string text) => "'" + text.Replace("'", "'\\''") + "'";
}
