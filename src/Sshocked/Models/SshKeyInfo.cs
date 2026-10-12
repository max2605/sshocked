namespace Sshocked.Models;

/// <summary>Public metadata only. Private key contents and passphrases are never read.</summary>
public sealed record SshKeyInfo(string Path, string Algorithm, string Fingerprint, string Comment, string PublicKey, bool IsAgentLoaded = false);

public sealed record SshAgentStatus(bool IsAvailable, IReadOnlyList<SshKeyInfo> Keys, string Message);
