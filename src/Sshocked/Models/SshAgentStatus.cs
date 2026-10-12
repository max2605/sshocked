namespace Sshocked.Models;

public sealed record SshAgentStatus(bool IsAvailable, IReadOnlyList<SshKeyInfo> Keys, string Message);
