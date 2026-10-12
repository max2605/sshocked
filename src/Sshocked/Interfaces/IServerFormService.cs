using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IServerFormService
{
    ServerHost? Fill(AppConfig config, ServerHost? existing = null);
}
