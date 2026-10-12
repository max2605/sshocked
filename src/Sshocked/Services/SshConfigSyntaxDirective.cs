namespace Sshocked.Services;

internal sealed record SshConfigSyntaxDirective(string Keyword, string Value, string? RawValue = null);
