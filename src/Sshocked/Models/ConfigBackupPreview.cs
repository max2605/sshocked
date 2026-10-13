namespace Sshocked.Models;

public sealed class ConfigBackupPreview
{
    public ConfigBackupRestoreMode Mode { get; }
    public IReadOnlyList<ConfigBackupChange> Changes { get; }
    public IReadOnlyList<string> Conflicts { get; }
    public bool IsLocalRecovery { get; }
    internal string ExpectedFingerprint { get; }
    internal string CandidateJson { get; }

    internal ConfigBackupPreview(ConfigBackupRestoreMode mode, IReadOnlyList<ConfigBackupChange> changes,
        IReadOnlyList<string> conflicts, string expectedFingerprint, string candidateJson, bool isLocalRecovery = false)
    {
        Mode = mode;
        Changes = changes;
        Conflicts = conflicts;
        ExpectedFingerprint = expectedFingerprint;
        CandidateJson = candidateJson;
        IsLocalRecovery = isLocalRecovery;
    }
}
