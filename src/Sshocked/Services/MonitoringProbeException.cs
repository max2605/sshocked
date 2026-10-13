using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MonitoringProbeException(MonitorDataQuality quality, string message) : Exception(message)
{
    public MonitorDataQuality Quality { get; } = quality;
}
