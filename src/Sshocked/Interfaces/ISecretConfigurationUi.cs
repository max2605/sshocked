using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface ISecretConfigurationUi
{
    Task Show();
    void ConfigureHost(ServerHost host);
    SecretRef? Pick(SecretType type, SecretRef? current = null);
}
