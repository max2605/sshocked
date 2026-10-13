using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ILocalCredentialService
{
    bool IsSupported { get; }
    bool HasPassword(ServerHost host);
    void SavePassword(ServerHost host, string password);
    string? GetPassword(ServerHost host);
    void ClearPassword(ServerHost host);
    void ClearAll();
}
