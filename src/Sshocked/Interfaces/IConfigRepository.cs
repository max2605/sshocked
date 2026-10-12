using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IConfigRepository
{
    AppConfig Load();
    void Save(AppConfig config);
    void Update(Action<AppConfig> update)
    {
        var config = Load();
        update(config);
        Save(config);
    }
    event EventHandler? Saved { add { } remove { } }
}
