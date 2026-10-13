using Sshocked.Models;

namespace Sshocked.Services;

public static class MonitoringProbeResult
{
    public static void Ensure(ProcessResult result)
    {
        if (result.ExitCode == 0) return;
        var error = result.StandardError;
        if (result.ExitCode == 255)
            throw new MonitoringProbeException(
                error.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase)
                    ? MonitorDataQuality.AuthenticationRequired : MonitorDataQuality.Error,
                "SSH access failed. Check credentials, known host keys and the connection route.");
        if (error.Contains("sudo", StringComparison.OrdinalIgnoreCase) &&
            (error.Contains("password", StringComparison.OrdinalIgnoreCase) || error.Contains("not allowed", StringComparison.OrdinalIgnoreCase)))
            throw new MonitoringProbeException(MonitorDataQuality.PermissionDenied, "The probe requires configured noninteractive sudo permissions.");
        if (result.ExitCode is 126 or 127)
            throw new MonitoringProbeException(MonitorDataQuality.Unsupported, "A required remote command is unavailable.");
        if (error.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) || error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            throw new MonitoringProbeException(MonitorDataQuality.PermissionDenied, "Remote probe access was denied.");
        throw new MonitoringProbeException(MonitorDataQuality.Error, $"The remote probe returned exit code {result.ExitCode}.");
    }

    public static MonitoringCommandOptions Options(MonitorDefinition definition, bool sudo = false) =>
        new(definition.MaxOutputBytes, definition.TimeoutSeconds, sudo);
}
