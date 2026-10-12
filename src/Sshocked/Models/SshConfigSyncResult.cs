namespace Sshocked.Models;

public sealed record SshConfigSyncResult(int Added, int Updated, int Skipped, int Conflicts)
{
    public int Imported => Added + Updated;
}
