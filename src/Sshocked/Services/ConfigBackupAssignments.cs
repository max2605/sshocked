using Sshocked.Models;

namespace Sshocked.Services;

internal static class ConfigBackupAssignments
{
    public static void Replace(AppConfig current, AppConfig replacement)
    {
        current.Groups = replacement.Groups;
        current.Hosts = replacement.Hosts;
        current.IsFirstStart = replacement.IsFirstStart;
        current.DefaultConnectionStrategy = replacement.DefaultConnectionStrategy;
        current.TrackRecentConnections = replacement.TrackRecentConnections;
        current.MaxRecentConnections = replacement.MaxRecentConnections;
        current.RecentConnections = replacement.RecentConnections;
        current.Audit = replacement.Audit;
        current.SshConfigSync = replacement.SshConfigSync;
        current.SecretProviders = replacement.SecretProviders;
        current.GlobalHooks = replacement.GlobalHooks;
        current.SessionRecording = replacement.SessionRecording;
        current.UI = replacement.UI;
        current.Macros = replacement.Macros;
        current.Monitoring = replacement.Monitoring;
        current.TargetViews = replacement.TargetViews;
        current.ForwardingProfiles = replacement.ForwardingProfiles;
    }
}
