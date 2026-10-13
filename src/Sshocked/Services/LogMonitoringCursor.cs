namespace Sshocked.Services;

internal sealed record LogMonitoringCursor(string Identity, long Offset, int PrefixLength, string PrefixHash,
    long AnchorOffset, int AnchorLength, string AnchorHash, bool SkipUntilLf, long Epoch);
