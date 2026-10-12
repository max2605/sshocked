using System.Globalization;
using System.Text.RegularExpressions;
using Sshocked.Models;

namespace Sshocked.Services;

public static class HookDefinition
{
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

    public static HookScript? SelectHook(ConnectionHooks? hooks, HookPhase phase) => phase switch
    {
        HookPhase.PreConnect => hooks?.PreConnect,
        HookPhase.PostConnect => hooks?.PostConnect,
        HookPhase.PreDisconnect => hooks?.PreDisconnect,
        HookPhase.PostDisconnect => hooks?.PostDisconnect,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static string PhaseName(HookPhase phase) => phase switch
    {
        HookPhase.PreConnect => HookConstants.PreConnect,
        HookPhase.PostConnect => HookConstants.PostConnect,
        HookPhase.PreDisconnect => HookConstants.PreDisconnect,
        HookPhase.PostDisconnect => HookConstants.PostDisconnect,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static Dictionary<string, string> BuildEnvironment(ServerHost host, HookPhase phase, HookContext context,
        HookScript? hook = null)
    {
        var environment = hook is null ? new Dictionary<string, string>() : new Dictionary<string, string>(hook.Environment);
        environment[HookConstants.HookType] = PhaseName(phase);
        environment[HookConstants.HostId] = host.Id;
        environment[HookConstants.HostAlias] = host.Alias;
        environment[HookConstants.HostName] = host.HostName;
        environment[HookConstants.HostPort] = host.Port.ToString(CultureInfo.InvariantCulture);
        environment[HookConstants.HostUser] = host.User;
        environment[HookConstants.HostGroup] = context.GroupName;
        environment[HookConstants.HostTags] = string.Join(',', host.Tags);
        environment[HookConstants.ConnectionTimestamp] = context.ConnectionTimestamp.ToString("O", CultureInfo.InvariantCulture);
        environment[HookConstants.ConfigDirectory] = context.ConfigDirectory;
        if (context.ExitCode is { } exitCode)
            environment[HookConstants.ExitCode] = exitCode.ToString(CultureInfo.InvariantCulture);
        if (context.DurationSeconds is { } duration)
            environment[HookConstants.SessionDuration] = duration.ToString("0.###", CultureInfo.InvariantCulture);
        return environment;
    }

    public static void Validate(HookScript hook, HookPhase phase)
    {
        if (!Enum.IsDefined(hook.Mode) || !Enum.IsDefined(hook.OutputMode)) throw new ArgumentException("Unknown hook mode.");
        if (hook.TimeoutSeconds is < 1 or > HookConstants.MaximumTimeoutSeconds) throw new ArgumentException("Hook timeout must be between 1 and 86400 seconds.");
        if (hook.Mode == HookExecutionMode.RemoteCommand && phase is HookPhase.PreConnect or HookPhase.PostDisconnect)
            throw new ArgumentException("Pre-connect and post-disconnect hooks must run locally.");
        if (hook.Mode == HookExecutionMode.LocalScript && string.IsNullOrWhiteSpace(hook.ScriptPath))
            throw new ArgumentException("The hook script path is required.");
        if (hook.Mode == HookExecutionMode.InlineScript && string.IsNullOrWhiteSpace(hook.InlineScript))
            throw new ArgumentException("Inline hook content is required.");
        if (hook.Mode is HookExecutionMode.ShellCommand or HookExecutionMode.RemoteCommand && string.IsNullOrWhiteSpace(hook.Command))
            throw new ArgumentException("The hook command is required.");
        foreach (var variable in hook.Environment)
            if (!Regex.IsMatch(variable.Key, HookConstants.EnvironmentNamePattern, RegexOptions.CultureInvariant) || variable.Value.Contains('\0'))
                throw new ArgumentException("Hook environment variables must have valid names and contain no NUL characters.");
    }
}
