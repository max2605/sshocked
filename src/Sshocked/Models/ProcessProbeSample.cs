namespace Sshocked.Models;

public sealed record ProcessProbeSample(string Boot, double? Time, double? Frequency, double? Cores,
    bool Complete, IReadOnlyList<ProcessProbeInstance> Processes);
