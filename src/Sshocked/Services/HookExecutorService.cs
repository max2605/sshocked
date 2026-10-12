using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class HookExecutorService(IExternalProcessRunner processRunner, IAuditService auditService,
    ILogger<HookExecutorService> logger) : IHookExecutorService, IAsyncDisposable
{
    private readonly object backgroundGate = new();
    private readonly HashSet<Task> backgroundTasks = [];
    private readonly CancellationTokenSource shutdown = new();
    private int disposalStarted;

    public async Task<bool> RunLocalHooks(HookPhase phase, ServerHost host, GlobalHooks? globalHooks,
        HookContext context, CancellationToken cancellationToken = default)
    {
        foreach (var hook in GetOrderedHooks(phase, host, globalHooks))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(hook, phase);
            if (hook.Mode == HookExecutionMode.RemoteCommand) continue;
            if (!hook.Blocking)
            {
                // ExecuteAndHandle observes failures and always disposes/kills timed-out child processes.
                var task = ExecuteAndHandle(hook, phase, host, context, cancellationToken);
                lock (backgroundGate) backgroundTasks.Add(task);
                _ = ObserveBackground(task);
                continue;
            }
            if (!await ExecuteAndHandle(hook, phase, host, context, cancellationToken) && !hook.ContinueOnError)
                return false;
        }
        return true;
    }

    private async Task ObserveBackground(Task task)
    {
        try { await task; }
        catch (Exception exception) { logger.LogWarning("Background hook cleanup failed: {Error}", exception.Message); }
        finally { lock (backgroundGate) backgroundTasks.Remove(task); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposalStarted, 1) != 0) return;
        await shutdown.CancelAsync();
        Task[] pending;
        lock (backgroundGate) pending = backgroundTasks.ToArray();
        try { await Task.WhenAll(pending); }
        catch (Exception exception) { logger.LogWarning("Background hook shutdown failed: {Error}", exception.Message); }
        shutdown.Dispose();
    }

    public static IReadOnlyList<HookScript> GetOrderedHooks(HookPhase phase, ServerHost host, GlobalHooks? globalHooks)
    {
        var global = MatchesTags(globalHooks, host) ? SelectHook(globalHooks, phase) : null;
        var specific = SelectHook(host.Hooks, phase);
        var ordered = phase is HookPhase.PreConnect or HookPhase.PreDisconnect
            ? new[] { global, specific } : new[] { specific, global };
        return ordered.Where(hook => hook is { Enabled: true }).Select(hook => hook!).ToList();
    }

    public static bool MatchesTags(GlobalHooks? hooks, ServerHost host)
    {
        if (hooks is null) return false;
        var tags = new HashSet<string>(host.Tags, StringComparer.OrdinalIgnoreCase);
        if (hooks.ExcludeTags.Any(tags.Contains)) return false;
        return hooks.IncludeTags.Count == 0 || hooks.IncludeTags.Any(tags.Contains);
    }

    private static HookScript? SelectHook(ConnectionHooks? hooks, HookPhase phase) => phase switch
    {
        HookPhase.PreConnect => hooks?.PreConnect,
        HookPhase.PostConnect => hooks?.PostConnect,
        HookPhase.PreDisconnect => hooks?.PreDisconnect,
        HookPhase.PostDisconnect => hooks?.PostDisconnect,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static string PhaseName(HookPhase phase) => phase switch
    {
        HookPhase.PreConnect => "pre-connect", HookPhase.PostConnect => "post-connect",
        HookPhase.PreDisconnect => "pre-disconnect", HookPhase.PostDisconnect => "post-disconnect",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static Dictionary<string, string> BuildEnvironment(ServerHost host, HookPhase phase, HookContext context,
        HookScript? hook = null)
    {
        var environment = hook is null ? new Dictionary<string, string>() : new Dictionary<string, string>(hook.Environment);
        environment["SSHOCKED_HOOK_TYPE"] = PhaseName(phase);
        environment["SSHOCKED_HOST_ID"] = host.Id;
        environment["SSHOCKED_HOST_ALIAS"] = host.Alias;
        environment["SSHOCKED_HOST_NAME"] = host.HostName;
        environment["SSHOCKED_HOST_PORT"] = host.Port.ToString(CultureInfo.InvariantCulture);
        environment["SSHOCKED_HOST_USER"] = host.User;
        environment["SSHOCKED_HOST_GROUP"] = context.GroupName;
        environment["SSHOCKED_HOST_TAGS"] = string.Join(',', host.Tags);
        environment["SSHOCKED_CONNECTION_TIMESTAMP"] = context.ConnectionTimestamp.ToString("O", CultureInfo.InvariantCulture);
        environment["SSHOCKED_CONFIG_DIR"] = context.ConfigDirectory;
        if (context.ExitCode is { } exitCode)
            environment["SSHOCKED_EXIT_CODE"] = exitCode.ToString(CultureInfo.InvariantCulture);
        if (context.DurationSeconds is { } duration)
            environment["SSHOCKED_SESSION_DURATION_SECONDS"] = duration.ToString("0.###", CultureInfo.InvariantCulture);
        return environment;
    }

    private async Task<bool> ExecuteAndHandle(HookScript hook, HookPhase phase, ServerHost host,
        HookContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string? temporaryScript = null;
        int? exitCode = null;
        var success = false;
        try
        {
            var environment = BuildEnvironment(host, phase, context, hook);
            var startInfo = BuildLocalStartInfo(hook, environment, out temporaryScript);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(hook.TimeoutSeconds));
            var result = await processRunner.RunAsync(startInfo, timeout.Token);
            exitCode = result.ExitCode;
            success = exitCode == 0;
            PublishOutput(hook.OutputMode, result.StandardOutput, result.StandardError);
            if (!success)
                logger.LogWarning("{Phase} hook for {Alias} exited with code {ExitCode}", PhaseName(phase), host.Alias, exitCode);
            else logger.LogInformation("{Phase} hook for {Alias} completed", PhaseName(phase), host.Alias);
            return success;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !shutdown.IsCancellationRequested)
        {
            logger.LogWarning("{Phase} hook for {Alias} timed out after {Timeout} seconds", PhaseName(phase), host.Alias, hook.TimeoutSeconds);
            return false;
        }
        catch (OperationCanceledException)
        {
            // Detached hooks must observe cancellation too, without leaving unobserved task exceptions.
            if (hook.Blocking) throw;
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning("{Phase} hook for {Alias} failed: {Error}", PhaseName(phase), host.Alias, exception.Message);
            return false;
        }
        finally
        {
            if (temporaryScript is not null)
            {
                try { File.Delete(temporaryScript); }
                catch (IOException exception) { logger.LogWarning("Could not remove temporary hook script: {Error}", exception.Message); }
                catch (UnauthorizedAccessException exception) { logger.LogWarning("Could not remove temporary hook script: {Error}", exception.Message); }
            }
            auditService.Record(new AuditEntry
            {
                EventType = "hook", Category = "hook", HostId = host.Id, Alias = host.Alias,
                Action = $"{PhaseName(phase)} ({hook.Mode})", Success = success, ExitCode = exitCode,
                DurationSeconds = stopwatch.Elapsed.TotalSeconds
            });
        }
    }

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
                arguments.Add(Regex.Replace(argument, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", match =>
                    environment.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value));
        }
        else throw new ArgumentException("Remote commands must run inside the SSH session wrapper.");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach (var variable in environment) startInfo.Environment[variable.Key] = variable.Value;
        return startInfo;
    }

    private void PublishOutput(HookOutputMode mode, string output, string error)
    {
        if (mode is HookOutputMode.Log or HookOutputMode.Both)
        {
            if (!string.IsNullOrEmpty(output)) logger.LogInformation("Hook output: {Output}", output.TrimEnd());
            if (!string.IsNullOrEmpty(error)) logger.LogWarning("Hook error output: {Output}", error.TrimEnd());
        }
        if (mode is HookOutputMode.Console or HookOutputMode.Both)
        {
            if (!string.IsNullOrEmpty(output)) System.Console.Write(output);
            if (!string.IsNullOrEmpty(error)) System.Console.Error.Write(error);
        }
    }

    public string? BuildRemoteSessionCommand(ServerHost host, GlobalHooks? globalHooks, HookContext context)
    {
        var postConnect = GetOrderedHooks(HookPhase.PostConnect, host, globalHooks)
            .Where(hook => hook.Mode == HookExecutionMode.RemoteCommand).ToList();
        var preDisconnect = GetOrderedHooks(HookPhase.PreDisconnect, host, globalHooks)
            .Where(hook => hook.Mode == HookExecutionMode.RemoteCommand).ToList();
        if (postConnect.Count == 0 && preDisconnect.Count == 0) return null;
        var command = new StringBuilder();
        foreach (var hook in postConnect) AppendRemoteHook(command, hook, HookPhase.PostConnect, host, context);
        command.AppendLine("\"${SHELL:-/bin/sh}\" -i");
        command.AppendLine("_sshocked_session_exit=$?");
        foreach (var hook in preDisconnect) AppendRemoteHook(command, hook, HookPhase.PreDisconnect, host, context);
        command.AppendLine("exit \"$_sshocked_session_exit\"");
        // The command is parsed by the remote POSIX shell even when sshocked runs on Windows.
        return command.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private void AppendRemoteHook(StringBuilder command, HookScript hook, HookPhase phase, ServerHost host, HookContext context)
    {
        Validate(hook, phase);
        logger.LogInformation("Scheduling remote {Phase} hook for {Alias}; remote commands inherit the SSH terminal and do not enforce TimeoutSeconds", PhaseName(phase), host.Alias);
        var block = new StringBuilder();
        foreach (var variable in BuildEnvironment(host, phase, context, hook))
            block.Append("export ").Append(variable.Key).Append('=').Append(RemoteShell.Quote(variable.Value)).AppendLine();
        if (phase == HookPhase.PreDisconnect) block.AppendLine("export SSHOCKED_EXIT_CODE=\"$_sshocked_session_exit\"");
        block.Append("eval ").Append(RemoteShell.Quote(hook.Command! + (string.IsNullOrWhiteSpace(hook.Arguments) ? "" : " " + hook.Arguments)));
        if (hook.OutputMode == HookOutputMode.Suppress) block.Append(" >/dev/null 2>&1");
        block.AppendLine();
        block.AppendLine("_sshocked_hook_exit=$?");
        block.AppendLine("if [ \"$_sshocked_hook_exit\" -ne 0 ]; then");
        block.Append("  printf '%s\\n' ").Append(RemoteShell.Quote($"sshocked {PhaseName(phase)} hook failed")).AppendLine(" >&2");
        if (!hook.ContinueOnError) block.AppendLine("  exit \"$_sshocked_hook_exit\"");
        block.AppendLine("fi");
        if (hook.Blocking) command.Append(block);
        else command.AppendLine("(").Append(block).AppendLine(") &");
        auditService.Record(new AuditEntry
        {
            EventType = "hook-scheduled", Category = "hook", HostId = host.Id, Alias = host.Alias,
            Action = $"{PhaseName(phase)} (RemoteCommand)", Success = true
        });
    }

    public static void Validate(HookScript hook, HookPhase phase)
    {
        if (!Enum.IsDefined(hook.Mode) || !Enum.IsDefined(hook.OutputMode)) throw new ArgumentException("Unknown hook mode.");
        if (hook.TimeoutSeconds is < 1 or > 86400) throw new ArgumentException("Hook timeout must be between 1 and 86400 seconds.");
        if (hook.Mode == HookExecutionMode.RemoteCommand && phase is HookPhase.PreConnect or HookPhase.PostDisconnect)
            throw new ArgumentException("Pre-connect and post-disconnect hooks must run locally.");
        if (hook.Mode == HookExecutionMode.LocalScript && string.IsNullOrWhiteSpace(hook.ScriptPath))
            throw new ArgumentException("The hook script path is required.");
        if (hook.Mode == HookExecutionMode.InlineScript && string.IsNullOrWhiteSpace(hook.InlineScript))
            throw new ArgumentException("Inline hook content is required.");
        if (hook.Mode is HookExecutionMode.ShellCommand or HookExecutionMode.RemoteCommand && string.IsNullOrWhiteSpace(hook.Command))
            throw new ArgumentException("The hook command is required.");
        foreach (var variable in hook.Environment)
            if (!Regex.IsMatch(variable.Key, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || variable.Value.Contains('\0'))
                throw new ArgumentException("Hook environment variables must have valid names and contain no NUL characters.");
    }
}
