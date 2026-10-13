using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ConfigBackupMenuService(IConfigBackupService backups, IPathInputService paths, IMenuFactory menus,
    IConsoleWriterService console, IConsoleHelperService helper) : IConfigBackupMenuService
{
    public async Task Show()
    {
        while (await menus.RunMenu("[bold]Configuration backup and restore[/]", [
            new("Export portable backup", 'E', executeAsync: Export),
            new("Inspect backup", 'I', executeAsync: Inspect),
            new("Preview and restore", 'R', executeAsync: Restore),
            new("Preview local recovery", 'L', executeAsync: Recover),
            new("Back", 'B', actionType: MenuActionType.Back)
        ])) { }
    }

    private Task Export()
    {
        if (paths.Prompt("Destination backup file") is { } path)
        {
            ConfigBackupPresentation.Inspect(backups.Export(path), console);
            console.WriteLine("Backup saved: " + Path.GetFullPath(path));
            helper.WaitForKey();
        }
        return Task.CompletedTask;
    }

    private Task Inspect()
    {
        if (paths.Prompt("Backup file") is { } path)
        { ConfigBackupPresentation.Inspect(backups.Inspect(path), console); helper.WaitForKey(); }
        return Task.CompletedTask;
    }

    private Task Restore()
    {
        if (paths.Prompt("Backup file") is not { } path) return Task.CompletedTask;
        var mode = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Restore mode:")
            .AddChoices("Merge", "Replace", "Back"));
        if (mode == "Back") return Task.CompletedTask;
        var preview = backups.Preview(path, mode == "Merge" ? ConfigBackupRestoreMode.Merge : ConfigBackupRestoreMode.Replace);
        ConfigBackupPresentation.Preview(preview, console);
        if (preview.Conflicts.Count > 0)
        { console.WriteLine("Resolve the listed conflicts or choose Replace, then preview again."); helper.WaitForKey(); return Task.CompletedTask; }
        if (!AnsiConsole.Confirm("Apply exactly this preview? A recovery backup will be saved first.", false)) return Task.CompletedTask;
        var result = backups.Apply(preview);
        console.WriteLine($"Configuration restored. Recovery backup: {result.RecoveryPath}");
        helper.WaitForKey();
        return Task.CompletedTask;
    }

    private Task Recover()
    {
        if (paths.Prompt("Private local recovery file") is not { } path) return Task.CompletedTask;
        var preview = backups.PreviewRecovery(path);
        ConfigBackupPresentation.Preview(preview, console);
        if (preview.Conflicts.Count > 0) { helper.WaitForKey(); return Task.CompletedTask; }
        if (!AnsiConsole.Confirm("Recover exactly these local settings? Another private snapshot will be saved first.", false)) return Task.CompletedTask;
        var result = backups.Apply(preview);
        console.WriteLine("Local configuration recovered. Previous-state snapshot: " + result.RecoveryPath);
        helper.WaitForKey();
        return Task.CompletedTask;
    }
}
