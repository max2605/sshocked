namespace Sshocked.Models;

public sealed record DockerBatchProgress(int Completed, int Total, string Target, DockerActionState? State);
