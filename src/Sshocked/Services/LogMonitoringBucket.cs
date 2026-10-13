namespace Sshocked.Services;

internal sealed record LogMonitoringBucket(long ObservedMilliseconds, long Lines, long Matches);
