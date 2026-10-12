namespace Sshocked.Models;

public enum SyncConflictResolution
{
    PromptUser,
    SshockedWins,
    OpenSshWins,
    KeepBoth,
    Skip
}
