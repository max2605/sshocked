namespace Sshocked.Models;

public class ConnectionHooks
{
    public HookScript? PreConnect { get; set; }
    public HookScript? PostConnect { get; set; }
    public HookScript? PreDisconnect { get; set; }
    public HookScript? PostDisconnect { get; set; }
}

public sealed class GlobalHooks : ConnectionHooks
{
    public List<string> IncludeTags { get; set; } = [];
    public List<string> ExcludeTags { get; set; } = [];
}

public sealed class HookScript
{
    public HookExecutionMode Mode { get; set; } = HookExecutionMode.ShellCommand;
    public string? ScriptPath { get; set; }
    public string? Command { get; set; }
    public string? InlineScript { get; set; }
    public string? Arguments { get; set; }
    public Dictionary<string, string> Environment { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public bool Blocking { get; set; } = true;
    public bool ContinueOnError { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public HookOutputMode OutputMode { get; set; } = HookOutputMode.Log;
}

public enum HookExecutionMode { LocalScript, RemoteCommand, ShellCommand, InlineScript }
public enum HookOutputMode { Log, Console, Both, Suppress }
public enum HookPhase { PreConnect, PostConnect, PreDisconnect, PostDisconnect }

public sealed record HookContext
{
    public DateTimeOffset ConnectionTimestamp { get; init; } = DateTimeOffset.UtcNow;
    public string ConfigDirectory { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
    public double? DurationSeconds { get; init; }
}
