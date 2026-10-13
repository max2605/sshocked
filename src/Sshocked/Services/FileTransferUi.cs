using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class FileTransferUi(IFileTransferService transferService, IPathInputService paths,
    IBatchMenuService? batches = null, IConfigRepository? repository = null)
{
    public async Task Upload(ServerHost host, bool quick = false)
    {
        var local = SelectLocalPath(quick);
        if (local is null) return;
        var recursive = Directory.Exists(local);
        var remote = PromptPath("Remote destination (directory ends in /)", "~/");
        if (remote is null) return;
        if (!AnsiConsole.Confirm($"Upload [yellow]{Markup.Escape(local)}[/] to [green]{Markup.Escape(host.Alias)}:{Markup.Escape(remote)}[/]?")) return;
        await RunTransfer(() => transferService.UploadFile(host, local, remote, recursive));
    }

    public async Task Download(ServerHost host)
    {
        var remote = PromptPath("Remote file or directory");
        if (remote is null) return;
        var recursive = AnsiConsole.Confirm("Copy a directory recursively?", false);
        await DownloadEntry(host, remote, recursive);
    }

    public async Task Browse(ServerHost host)
    {
        var path = PromptPath("Remote directory", "~");
        if (path is null) return;
        while (true)
        {
            List<RemoteFileEntry> entries;
            try { entries = await transferService.ListRemoteDirectory(host, path); }
            catch (Exception exception)
            {
                AnsiConsole.MarkupLine($"[red]Browse failed:[/] {Markup.Escape(exception.Message)}");
                path = PromptPath("Remote directory", "~");
                if (path is null) return;
                continue;
            }

            var table = new Table().AddColumn("Name").AddColumn("Type").AddColumn("Bytes").AddColumn("Modified");
            foreach (var entry in entries)
                table.AddRow(Markup.Escape(entry.Name), entry.IsDirectory ? "directory" : "file",
                    entry.Size.ToString(), entry.LastModified?.ToString("yyyy-MM-dd HH:mm") ?? "unknown");
            AnsiConsole.Write(table);
            var choices = new List<FileTransferUiBrowseChoice> { new("Back", null), new(".. (parent directory)", null), new("/ (root directory)", null) };
            choices.AddRange(entries.Select(entry => new FileTransferUiBrowseChoice(entry.Name + (entry.IsDirectory ? "/" : ""), entry)));
            var selected = AnsiConsole.Prompt(new SelectionPrompt<FileTransferUiBrowseChoice>()
                .Title($"[green]{Markup.Escape(host.Alias)}:{Markup.Escape(path)}[/]")
                .UseConverter(choice => Markup.Escape(choice.Label)).AddChoices(choices));
            if (selected == choices[0]) return;
            if (selected == choices[1]) { path = ParentPath(path); continue; }
            if (selected == choices[2]) { path = "/"; continue; }
            if (selected.Entry is not { } file) continue;
            if (file.IsDirectory)
            {
                var action = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .Title($"[green]{Markup.Escape(file.Name)}[/]")
                    .AddChoices(FileTransferLabels.EnterDirectory, FileTransferLabels.DownloadDirectory, FileTransferLabels.Cancel));
                if (action == FileTransferLabels.EnterDirectory) path = file.FullPath;
                else if (action == FileTransferLabels.DownloadDirectory) await DownloadEntry(host, file.FullPath, true);
            }
            else await DownloadEntry(host, file.FullPath, false);
        }
    }

    public async Task UploadGroup(IEnumerable<ServerHost> hosts)
    {
        var selectedHosts = hosts.ToList();
        if (batches is not null)
        {
            var ids = selectedHosts.Select(host => host.Id).ToHashSet(StringComparer.Ordinal);
            await batches.Upload(() => repository is null ? selectedHosts : repository.Load().Hosts.Where(host => ids.Contains(host.Id)).ToList());
            return;
        }
        if (selectedHosts.Count == 0) { AnsiConsole.MarkupLine("[yellow]This group has no servers.[/]"); return; }
        var local = SelectLocalPath(false);
        if (local is null) return;
        var remote = PromptPath("Remote destination on every server", "~/");
        if (remote is null || !AnsiConsole.Confirm($"Upload to [yellow]{selectedHosts.Count}[/] servers?")) return;
        await RunGroup(() => transferService.UploadToGroup(selectedHosts, local, remote, Directory.Exists(local)));
    }

    public async Task DownloadGroup(IEnumerable<ServerHost> hosts)
    {
        var selectedHosts = hosts.ToList();
        if (batches is not null)
        {
            var ids = selectedHosts.Select(host => host.Id).ToHashSet(StringComparer.Ordinal);
            await batches.Download(() => repository is null ? selectedHosts : repository.Load().Hosts.Where(host => ids.Contains(host.Id)).ToList());
            return;
        }
        if (selectedHosts.Count == 0) { AnsiConsole.MarkupLine("[yellow]This group has no servers.[/]"); return; }
        var remote = PromptPath("Remote file or directory on every server");
        if (remote is null) return;
        var local = paths.Prompt("Local destination directory", validate: value => Directory.Exists(value)
            ? null : "The directory does not exist.");
        if (local is null) return;
        var recursive = AnsiConsole.Confirm("Copy directories recursively?", false);
        if (!AnsiConsole.Confirm($"Download from [yellow]{selectedHosts.Count}[/] servers using separate filenames?")) return;
        await RunGroup(() => transferService.DownloadFromGroup(selectedHosts, remote, local, recursive));
    }

    private async Task DownloadEntry(ServerHost host, string remote, bool recursive)
    {
        var leaf = remote.TrimEnd('/').Split('/').Last();
        var local = PromptPath("Local destination", Path.Combine(Environment.CurrentDirectory, leaf is "~" or "" ? "download" : leaf));
        if (local is null) return;
        if ((File.Exists(local) || Directory.Exists(local)) && !AnsiConsole.Confirm("The destination exists. Allow SCP to replace or merge it?", false)) return;
        if (!AnsiConsole.Confirm($"Download [yellow]{Markup.Escape(remote)}[/] from [green]{Markup.Escape(host.Alias)}[/]?")) return;
        await RunTransfer(() => transferService.DownloadFile(host, remote, local, recursive));
    }

    private string? SelectLocalPath(bool quick)
    {
        if (quick)
        {
            var files = Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory).Order(StringComparer.OrdinalIgnoreCase).ToList();
            var choices = new List<FileTransferUiLocalChoice> { new("Enter a path...", null), new(FileTransferLabels.Cancel, null) };
            choices.AddRange(files.Select(file => new FileTransferUiLocalChoice(Path.GetFileName(file) + (Directory.Exists(file) ? "/" : ""), file)));
            var selected = AnsiConsole.Prompt(new SelectionPrompt<FileTransferUiLocalChoice>().Title("[green]Local file or directory[/]")
                .UseConverter(choice => Markup.Escape(choice.Label)).AddChoices(choices));
            if (selected == choices[1]) return null;
            if (selected.Path is not null) return selected.Path;
        }
        return paths.Prompt("Local file or directory", validate: value => File.Exists(value) || Directory.Exists(value)
            ? null : "The file or directory does not exist.");
    }

    private string? PromptPath(string label, string? defaultValue = null) => paths.Prompt(label, defaultValue);

    private static async Task RunTransfer(Func<Task<int>> transfer)
    {
        var interactive = AnsiConsole.Profile.Capabilities.Interactive;
        try
        {
            AnsiConsole.Profile.Capabilities.Interactive = false;
            System.Console.ResetColor();
            var exitCode = await transfer();
            AnsiConsole.MarkupLine(exitCode == 0 ? "[green]Transfer completed.[/]" : $"[red]Transfer failed (exit code {exitCode}).[/]");
        }
        catch (Exception exception) { AnsiConsole.MarkupLine($"[red]Transfer failed:[/] {Markup.Escape(exception.Message)}"); }
        finally
        {
            AnsiConsole.Profile.Capabilities.Interactive = interactive;
            System.Console.ResetColor();
            AcknowledgeResult(interactive);
        }
    }

    private static async Task RunGroup(Func<Task<List<FileTransferResult>>> transfer)
    {
        var interactive = AnsiConsole.Profile.Capabilities.Interactive;
        try
        {
            AnsiConsole.Profile.Capabilities.Interactive = false;
            var results = await transfer();
            for (var index = 0; index < results.Count; index++)
            {
                var result = results[index];
                var status = result.Success ? "[green]OK[/]" : "[red]FAILED[/]";
                AnsiConsole.MarkupLine($"[[{index + 1}/{results.Count}]] {Markup.Escape(result.Host.Alias)}: {status} — {Markup.Escape(result.LocalPath)}");
                if (result.Error is not null) AnsiConsole.MarkupLine($"[red]{Markup.Escape(result.Error)}[/]");
            }
        }
        catch (Exception exception) { AnsiConsole.MarkupLine($"[red]Group transfer failed:[/] {Markup.Escape(exception.Message)}"); }
        finally
        {
            AnsiConsole.Profile.Capabilities.Interactive = interactive;
            System.Console.ResetColor();
            AcknowledgeResult(interactive);
        }
    }

    private static void AcknowledgeResult(bool interactive)
    {
        if (!interactive || System.Console.IsInputRedirected || System.Console.IsOutputRedirected) return;
        AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
        System.Console.ReadKey(intercept: true);
    }

    public static string ParentPath(string path)
    {
        if (path == "/") return "/";
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        if (slash > 0) return trimmed[..slash];
        if (slash == 0) return "/";
        return path is "~" or "." ? path + "/.." : ".";
    }
}
