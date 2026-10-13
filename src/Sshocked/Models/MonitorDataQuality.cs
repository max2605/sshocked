namespace Sshocked.Models;

public enum MonitorDataQuality
{
    Unchecked, Success, Baseline, Partial, AuthenticationRequired, PermissionDenied,
    Unsupported, Timeout, Error, Stale, Disabled
}
