using Sshocked.Models;

namespace Sshocked.Services;

internal static class AuditPolicy
{
    public static bool CategoryEnabled(AuditSettings settings, string category) => category switch
    {
        "connection" => settings.LogConnections,
        "command" or "docker" => settings.LogCommands,
        "filetransfer" => settings.LogFileTransfers,
        "config" => settings.LogConfigChanges,
        "secret" => settings.LogSecretAccess,
        _ => true
    };
}
