namespace Sshocked.Models;

public sealed record SshConfigExportResult(int Exported, bool Changed, IReadOnlyList<string> WrittenPaths, IReadOnlyList<string> Backups);
