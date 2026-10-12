namespace Sshocked.Services;

internal sealed record SshConfigImporterStanza(string[] Patterns, List<SshConfigSyntaxDirective> Directives, string? Id = null);
