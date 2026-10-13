namespace Sshocked.Services;

internal sealed record LogMonitoringRead(string Identity, long Size, long Start, int PrefixLength, string PrefixHash,
    long AnchorOffset, int AnchorLength, string AnchorHash, string Reset, bool EndsWithLf, byte[] Bytes);
