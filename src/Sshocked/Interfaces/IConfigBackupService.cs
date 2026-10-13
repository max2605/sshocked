using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IConfigBackupService
{
    ConfigBackupDocument Export(string path);
    ConfigBackupDocument Inspect(string path);
    ConfigBackupPreview Preview(string path, ConfigBackupRestoreMode mode);
    ConfigBackupPreview PreviewRecovery(string path);
    ConfigBackupRestoreResult Apply(ConfigBackupPreview preview, string? recoveryPath = null);
}
