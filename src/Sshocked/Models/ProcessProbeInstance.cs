namespace Sshocked.Models;

public sealed record ProcessProbeInstance(string Pid, string Started, double? CpuTicks, double? MemoryBytes);
