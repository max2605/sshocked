namespace Sshocked.Models;

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
