namespace Sshocked.Services;

internal static class SshConnectionConstants
{
    public const string Executable = "ssh";
    public const string CommandExecutedEvent = "command.executed";
    public const string CommandAuditCategory = "command";
    public const string DockerAuditCategory = "docker";
    public const string DockerCommandPrefix = "docker ";
    public const string LoopbackAddress = "127.0.0.1";
    public const string BastionHostKeyPrefix = "bastion:";
    public const string Redacted = "[REDACTED]";
}
