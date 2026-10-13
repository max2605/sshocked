using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class ConfigBackupPresentation
{
    public static void Inspect(ConfigBackupDocument backup, IConsoleWriterService console)
    {
        console.WriteLine($"Backup schema {backup.SchemaVersion}; created {backup.CreatedAtUtc:O}; application {AuditRedactor.Redact(backup.ApplicationVersion)}.");
        console.WriteLine($"Hosts: {backup.Configuration.Hosts.Count}; groups: {backup.Configuration.Groups.Count}; macros: {backup.Configuration.Macros.Count}; views: {backup.Configuration.TargetViews.Count}; monitors: {backup.Configuration.Monitoring.Monitors.Count}; forwarding profiles: {backup.Configuration.ForwardingProfiles.Count}.");
        console.WriteLine($"Redacted values: {backup.RedactedValues}. Passwords, local credential files, private key contents and hook environments are excluded.");
        console.WriteLine("Local passwords must be saved again for a different account or endpoint. Review redacted commands and hook settings after restoring.");
    }

    public static void Preview(ConfigBackupPreview preview, IConsoleWriterService console)
    {
        console.WriteLine($"Restore mode: {preview.Mode}; {preview.Changes.Count} changes.");
        if (preview.IsLocalRecovery)
            console.WriteLine("Confidential local recovery snapshot: exact persisted settings, including hook environments and inline configuration text. Not a portable backup.");
        foreach (var change in preview.Changes)
            console.WriteLine($"{change.Change}: {change.Category} {AuditRedactor.Redact(change.Name)} ({AuditRedactor.Redact(change.Id)})");
        foreach (var conflict in preview.Conflicts) console.WriteLine("Conflict: " + conflict);
        console.WriteLine("Preview fingerprint: " + preview.ExpectedFingerprint);
        console.WriteLine(preview.Mode == ConfigBackupRestoreMode.Merge
            ? "Merge updates records with matching IDs and adds new records; existing application settings and unrelated records remain."
            : "Replace uses the entire backup configuration and removes records absent from it.");
    }
}
