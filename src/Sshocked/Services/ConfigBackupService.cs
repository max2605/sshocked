using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ConfigBackupService(IConfigRepository repository, IAuditService audit, string? recoveryDirectory = null) : IConfigBackupService
{
    private readonly string recoveryRoot = Path.GetFullPath(recoveryDirectory ?? Path.Combine(ConfigPaths.DirectoryPath, "recovery"));
    public ConfigBackupDocument Export(string path)
    {
        var backup = ConfigBackupSanitizer.Create(repository.Load());
        ConfigBackupFiles.Write(path, JsonSerializer.Serialize(backup, ConfigBackupJsonContext.Default.ConfigBackupDocument));
        audit.Record(new() { Category = "config", EventType = "config.backup.exported", Action = "Portable configuration backup" });
        return backup;
    }

    public ConfigBackupDocument Inspect(string path)
    {
        try
        {
            var backup = JsonSerializer.Deserialize(ConfigBackupFiles.Read(path), ConfigBackupJsonContext.Default.ConfigBackupDocument)
                ?? throw new InvalidDataException("The backup contains no document.");
            if (backup.Format != "sshocked-configuration" || backup.SchemaVersion != 1)
                throw new InvalidDataException("Unsupported backup format or schema version. Expected sshocked-configuration version 1.");
            var errors = ConfigBackupValidation.Conflicts(backup.Configuration);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            var portable = ConfigBackupSanitizer.Create(backup.Configuration);
            backup.Configuration = portable.Configuration;
            backup.RedactedValues += portable.RedactedValues;
            return backup;
        }
        catch (JsonException) { throw new InvalidDataException("The backup contains malformed JSON or required fields are missing."); }
        catch (NullReferenceException) { throw new InvalidDataException("The backup contains invalid null configuration sections."); }
    }

    public ConfigBackupPreview Preview(string path, ConfigBackupRestoreMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Choose merge or replace.");
        var incoming = Inspect(path).Configuration;
        var current = repository.Load();
        var candidate = ConfigBackupPlanner.Candidate(current, incoming, mode);
        var conflicts = ConfigBackupValidation.Conflicts(candidate);
        return new(mode, ConfigBackupPlanner.Changes(current, candidate), conflicts.AsReadOnly(), Fingerprint(current),
            JsonSerializer.Serialize(candidate, ConfigBackupJsonContext.Default.AppConfig));
    }

    public ConfigBackupPreview PreviewRecovery(string path)
    {
        try
        {
            var document = JsonSerializer.Deserialize(ConfigBackupRecoveryFiles.Read(recoveryRoot, path), ConfigBackupJsonContext.Default.ConfigBackupLocalRecoveryDocument)
                ?? throw new InvalidDataException("The recovery file contains no document.");
            if (document.Format != "sshocked-local-recovery" || document.SchemaVersion != 1 || document.UserBinding != ConfigBackupRecoveryFiles.Binding())
                throw new InvalidDataException("Unsupported local recovery version or the snapshot belongs to another account or computer.");
            var conflicts = ConfigBackupValidation.Conflicts(document.Configuration);
            var current = repository.Load();
            return new(ConfigBackupRestoreMode.Replace, ConfigBackupPlanner.Changes(current, document.Configuration), conflicts.AsReadOnly(),
                Fingerprint(current), JsonSerializer.Serialize(document.Configuration, ConfigBackupJsonContext.Default.AppConfig), isLocalRecovery: true);
        }
        catch (JsonException) { throw new InvalidDataException("The recovery file contains malformed JSON or required fields are missing."); }
        catch (NullReferenceException) { throw new InvalidDataException("The recovery file contains invalid null configuration sections."); }
    }

    public ConfigBackupRestoreResult Apply(ConfigBackupPreview preview, string? recoveryPath = null)
    {
        if (preview.Conflicts.Count > 0) throw new InvalidOperationException("Resolve the preview conflicts or choose replace before restoring.");
        var path = ConfigBackupRecoveryFiles.PathFor(recoveryRoot, recoveryPath);
        repository.Update(current =>
        {
            if (Fingerprint(current) != preview.ExpectedFingerprint)
                throw new InvalidOperationException("Configuration changed after preview. Generate a new preview before restoring.");
            var recovery = new ConfigBackupLocalRecoveryDocument { UserBinding = ConfigBackupRecoveryFiles.Binding(), Configuration = current };
            ConfigBackupRecoveryFiles.Write(recoveryRoot, path,
                JsonSerializer.Serialize(recovery, ConfigBackupJsonContext.Default.ConfigBackupLocalRecoveryDocument));
            var replacement = JsonSerializer.Deserialize(preview.CandidateJson, ConfigBackupJsonContext.Default.AppConfig)
                ?? throw new InvalidDataException("The restore candidate contains no configuration.");
            ConfigBackupAssignments.Replace(current, replacement);
        });
        audit.Record(new() { Category = "config", EventType = "config.backup.restored", Action = $"{(preview.IsLocalRecovery ? "Local recovery" : preview.Mode.ToString())}; {preview.Changes.Count} changes" });
        return new(path, preview.Changes.Count);
    }

    private static string Fingerprint(AppConfig config) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config, ConfigBackupJsonContext.Default.AppConfig))));
}
