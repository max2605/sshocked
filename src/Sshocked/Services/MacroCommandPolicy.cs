using Sshocked.Models;

namespace Sshocked.Services;

public static class MacroCommandPolicy
{
    public const int MaximumCommands = 1000;
    public const int MaximumCommandLength = 16384;
    public const int MaximumNameLength = 80;

    public static string NormalizeName(string name)
    {
        if (name.Any(char.IsControl)) throw new ArgumentException($"Use a macro name of 1 to {MaximumNameLength} characters without control characters.");
        name = name.Trim();
        if (name.Length is < 1 or > MaximumNameLength)
            throw new ArgumentException($"Use a macro name of 1 to {MaximumNameLength} characters.");
        return name;
    }

    public static void Validate(string command, ServerHost? host = null)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Contains('\0') || command.Length > MaximumCommandLength)
            throw new ArgumentException($"Enter a non-empty command of at most {MaximumCommandLength} characters without NUL characters.");
        // Replacing secrets would silently alter executable content. Reject recognized secret
        // literals instead; protected credential prompts never enter the command input loop.
        if (AuditRedactor.Redact(command) != command || new[] { host?.SshPassword, host?.SudoPassword }
            .Any(secret => !string.IsNullOrEmpty(secret) && command.Contains(secret, StringComparison.Ordinal)))
            throw new ArgumentException("Commands containing recognized password or token literals cannot be recorded. Use remote credential references instead.");
    }
}
