namespace Sshocked.Services;

internal sealed record DockerMonitoringContainer(string Id, string Name, string Status, bool Running, int ExitCode,
    bool OomKilled, long RestartCount, string Health, long? MemoryLimit, string? Project, string? Service);
