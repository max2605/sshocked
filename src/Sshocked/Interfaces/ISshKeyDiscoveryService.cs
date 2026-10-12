using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISshKeyDiscoveryService
{
    IReadOnlyList<SshKeyInfo> ListAvailableKeys();
    SshAgentStatus GetAgentStatus();
    SshKeyInfo? ParsePublicKey(string publicKey, string privateKeyPath = "");
}
