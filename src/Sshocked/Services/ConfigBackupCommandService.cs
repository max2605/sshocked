using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ConfigBackupCommandService(IConfigBackupService backups, IConsoleWriterService console) : IManagementCommandHandler
{
    public bool Handles(string command) => command.Equals("backup", StringComparison.OrdinalIgnoreCase);

    public Task Execute(string command, string[] arguments)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant();
        var options = new CommandOptions(arguments.Length == 0 ? [] : arguments[1..], ["mode", "recovery", "expected"], ["apply"]);
        if (options.Positionals.Count != 1) throw new ArgumentException("Use backup export|inspect|preview|restore|recover FILE [--mode merge|replace] [--apply] [--expected FINGERPRINT] [--recovery NAME].");
        var path = options.Positionals[0];
        switch (action)
        {
            case "export":
                RequireSimple(options);
                ConfigBackupPresentation.Inspect(backups.Export(path), console);
                console.WriteLine("Backup saved: " + Path.GetFullPath(path));
                break;
            case "inspect":
                RequireSimple(options);
                ConfigBackupPresentation.Inspect(backups.Inspect(path), console);
                break;
            case "preview":
            case "restore":
            case "recover":
                if (action == "preview" && (options.Has("apply") || options.Has("recovery") || options.Has("expected")))
                    throw new ArgumentException("Use backup preview FILE --mode merge|replace.");
                if (action == "recover" && options.Has("mode")) throw new ArgumentException("Local recover always restores the exact snapshot with Replace; omit --mode.");
                var mode = action == "recover" ? ConfigBackupRestoreMode.Replace : options.Get("mode")?.ToLowerInvariant() switch
                {
                    "merge" => ConfigBackupRestoreMode.Merge,
                    "replace" => ConfigBackupRestoreMode.Replace,
                    _ => throw new ArgumentException("Select --mode merge or --mode replace.")
                };
                var preview = action == "recover" ? backups.PreviewRecovery(path) : backups.Preview(path, mode);
                ConfigBackupPresentation.Preview(preview, console);
                if (action is "restore" or "recover" && options.Has("apply"))
                {
                    if (options.Get("expected") is { } expected && !expected.Equals(preview.ExpectedFingerprint, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Configuration changed since the selected preview. Generate and review a new preview.");
                    var result = backups.Apply(preview, options.Get("recovery"));
                    console.WriteLine($"Configuration restored; {result.Changes} changes. Recovery backup: {result.RecoveryPath}");
                }
                else console.WriteLine("No changes applied. Restore requires --apply after reviewing the preview.");
                break;
            default: throw new ArgumentException("Use backup export|inspect|preview|restore|recover FILE.");
        }
        return Task.CompletedTask;
    }

    private static void RequireSimple(CommandOptions options)
    {
        if (options.Has("mode") || options.Has("apply") || options.Has("recovery") || options.Has("expected"))
            throw new ArgumentException("Export and inspect accept only the backup file path.");
    }
}
