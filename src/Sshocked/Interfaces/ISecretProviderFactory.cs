using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISecretProviderFactory
{
    string ProviderType { get; }

    ISecretProvider Create(SecretProviderConfig config);
}
