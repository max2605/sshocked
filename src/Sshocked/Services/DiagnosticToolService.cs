using System.Diagnostics;
using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class DiagnosticToolService(IExternalProcessRunner runner, Func<string, string?>? executableResolver = null) : IDiagnosticToolService
{
    public async Task<IReadOnlyList<DiagnosticTool>> Inspect(bool includePaths, CancellationToken cancellationToken = default)
    {
        var result = new List<DiagnosticTool>();
        foreach (var name in new[] { "ssh", "scp", "az" })
        {
            var path = Resolve(name);
            if (path is null) { result.Add(new(name, false, null, null, "Not found on PATH.")); continue; }
            if (name == "scp") { result.Add(new(name, true, null, includePaths ? path : null, "Uses the OpenSSH installation.")); continue; }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var info = name == "az" && path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                    ? AzureCliCommand.Create(["version", "--output", "json"])
                    : new ProcessStartInfo { FileName = path, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                if (info.ArgumentList.Count == 0)
                    foreach (var argument in name == "ssh" ? new[] { "-V" } : new[] { "version", "--output", "json" }) info.ArgumentList.Add(argument);
                var execution = await runner.RunBounded(info, 16384, cancellationToken: timeout.Token);
                var version = name == "az" ? AzureVersion(execution.StandardOutput)
                    : (execution.StandardOutput + execution.StandardError).Split('\n', '\r').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();
                result.Add(new(name, true, DiagnosticRedactor.Sanitize(version, [], includePaths), includePaths ? path : null,
                    execution.ExitCode == 0 ? null : $"Version check exited with code {execution.ExitCode}."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { result.Add(new(name, true, null, includePaths ? path : null, "Version check timed out.")); }
            catch (Exception error) { result.Add(new(name, true, null, includePaths ? path : null, "Version check failed: " + error.GetType().Name)); }
        }
        var terminals = OperatingSystem.IsWindows() ? new[] { "wt" } : OperatingSystem.IsMacOS() ? new[] { "osascript" }
            : new[] { "x-terminal-emulator", "gnome-terminal", "konsole", "xterm" };
        foreach (var name in terminals)
        {
            var path = Resolve(name);
            result.Add(new(name, path is not null, null, includePaths ? path : null, "Availability check only; no terminal was opened."));
        }
        return result.AsReadOnly();
    }

    private string? Resolve(string command)
    {
        if (executableResolver is not null) return executableResolver(command);
        var names = OperatingSystem.IsWindows() ? new[] { command + ".exe", command + ".cmd", command } : new[] { command };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            var path = directory.Trim().Trim('"');
            if (path.Length == 0) continue;
            foreach (var name in names)
            {
                var candidate = Path.Combine(path, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static string? AzureVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("azure-cli", out var version) ? version.GetString() : null;
    }
}
