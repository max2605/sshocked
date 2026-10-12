namespace Sshocked.Interfaces;

public sealed record ParseResult
{
    public bool HasPositionalArg { get; init; }

    public string? PositionalArg { get; init; }

    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    public bool ShowList { get; init; }

    public bool HasGroupCommand { get; init; }

    public string? GroupName { get; init; }

    public string? Command { get; init; }

    public string? ManagementCommand { get; init; }
    public string[] CommandArguments { get; init; } = [];
    public bool IsInteractive => !HasPositionalArg && !ShowHelp && !ShowVersion && !ShowList && !HasGroupCommand && ManagementCommand is null;
}
