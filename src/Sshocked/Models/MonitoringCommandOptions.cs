namespace Sshocked.Models;

public sealed record MonitoringCommandOptions(int MaxOutputBytes = 1048576,
    int TimeoutSeconds = 15, bool AllowSudo = false);
