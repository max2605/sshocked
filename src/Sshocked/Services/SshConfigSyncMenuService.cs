using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SshConfigSyncMenuService(ISshConfigSyncService sync, ISshConfigImporter importer,
    IConfigRepository repository, IMenuFactory menus, IConsoleHelperService consoleHelper,
    IConsoleWriterService console, IPathInputService paths) : ISshConfigSyncMenuService
{
    public async Task Show()
    {
        var running = true;
        while (running)
        {
            var pending = sync.GetStatus().HasPendingChanges ? " (changes need attention)" : "";
            var settings = repository.Load().SshConfigSync;
            running = await menus.RunMenu("[bold yellow]SSH Config Sync[/]" + pending,
            [
                new("Import from OpenSSH config", 'I', executeAsync: () => Run(Import)),
                new("Export to OpenSSH config", 'E', executeAsync: () => Run(Export)),
                new("View sync status", 'V', executeAsync: () => Run(ShowStatus)),
                new("Configure sync settings", 'C', executeAsync: () => Run(Configure)),
                new(settings.AutoImportOnChange && settings.AutoExportOnChange ? "Disable auto-sync" : "Enable auto-sync", 'A', executeAsync: () => Run(ToggleAutoSync)),
                new("Back", 'B', actionType: MenuActionType.Back)
            ]);
        }
    }

    private Task Run(Action operation)
    {
        try { operation(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
        {
            console.WriteLine($"SSH config sync failed: {ex.Message}");
        }
        consoleHelper.WaitForKey();
        return Task.CompletedTask;
    }

    private void Import()
    {
        var hosts = importer.Import();
        console.WriteLine($"Found {hosts.Count} concrete host alias(es) in {SshConfigExporter.DefaultConfigPath} (including Include files).");
        foreach (var host in hosts) console.WriteLine($"  {host.Alias}: {host.User}@{host.HostName}:{host.Port}");
        if (!AnsiConsole.Confirm("Apply import? Existing host groups, tags and saved containers are retained.", defaultValue: false)) return;
        SyncConflictResolution? all = null;
        var result = sync.Import(resolveConflict: (existing, incoming) =>
        {
            if (all is not null) return all.Value;
            console.WriteLine($"Conflict for {incoming.Alias}:");
            console.WriteLine($"  sshocked: {existing.Alias}: {existing.User}@{existing.HostName}:{existing.Port}");
            console.WriteLine($"  OpenSSH:  {incoming.Alias}: {incoming.User}@{incoming.HostName}:{incoming.Port}");
            console.WriteLine($"  Identity: {existing.IdentityFile ?? "default"} -> {incoming.IdentityFile ?? "default"}");
            console.WriteLine($"  ProxyJump: {existing.ProxyJump ?? "none"} -> {incoming.ProxyJump ?? "none"}");
            console.WriteLine($"  Options: {existing.CustomSshOptions ?? "none"} -> {incoming.CustomSshOptions ?? "none"}");
            var resolution = AnsiConsole.Prompt(new SelectionPrompt<SyncConflictResolution>().Title("Resolve conflict:")
                .UseConverter(DescribeResolution).AddChoices(SyncConflictResolution.SshockedWins, SyncConflictResolution.OpenSshWins,
                    SyncConflictResolution.KeepBoth, SyncConflictResolution.Skip));
            if (AnsiConsole.Confirm("Use this choice for remaining conflicts?", defaultValue: false)) all = resolution;
            return resolution;
        });
        console.WriteLine($"Import complete: {result.Added} added, {result.Updated} updated, {result.Skipped} skipped, {result.Conflicts} conflict(s).");
    }

    private void Export()
    {
        var settings = repository.Load().SshConfigSync;
        console.WriteLine(settings.Strategy == ExportStrategy.MarkedBlocks
            ? $"Update the SSHOCKED marked section in {SshConfigExporter.DefaultConfigPath}. Other sections are preserved."
            : $"Update {SshConfigExporter.ResolveIncludePath(settings.IncludeFilePath)} and ensure its Include directive exists in the main SSH config.");
        if (!AnsiConsole.Confirm("Export now? Existing files receive a timestamped backup.", defaultValue: false)) return;
        var result = sync.Export();
        console.WriteLine($"Exported {result.Exported} host(s). {(result.Changed ? "Files updated." : "Already up to date.")}");
        foreach (var backup in result.Backups) console.WriteLine($"  Backup: {backup}");
    }

    private void ShowStatus()
    {
        var status = sync.GetStatus();
        var settings = status.Settings;
        console.WriteLine($"SSH config: {SshConfigExporter.DefaultConfigPath}");
        console.WriteLine($"Strategy: {settings.Strategy}");
        console.WriteLine($"Last export: {FormatTime(settings.LastExportUtc)} ({settings.LastExportCount} hosts)");
        console.WriteLine($"Last import: {FormatTime(settings.LastImportUtc)} ({settings.LastImportCount} hosts)");
        console.WriteLine($"Auto-export: {settings.AutoExportOnChange}; Auto-import: {settings.AutoImportOnChange}; Watching: {status.IsWatching}");
        console.WriteLine($"sshocked hosts: {status.ConfiguredHosts}; OpenSSH concrete aliases: {status.OpenSshHosts}");
        console.WriteLine($"Pending changes: {status.HasPendingChanges}");
        console.WriteLine($"Conflict policy: {DescribeResolution(settings.ConflictResolution)}");
        if (status.LastError is not null) console.WriteLine($"Needs attention: {status.LastError}");
        if (settings.ConflictResolution == SyncConflictResolution.PromptUser)
            console.WriteLine("Automatic imports defer conflicting hosts until you import from this menu.");
    }

    private void Configure()
    {
        var config = repository.Load();
        var settings = config.SshConfigSync;
        settings.Strategy = AnsiConsole.Prompt(new SelectionPrompt<ExportStrategy>().Title("Export strategy:")
            .AddChoices(ExportStrategy.MarkedBlocks, ExportStrategy.IncludeFile).DefaultValue(settings.Strategy));
        if (settings.Strategy == ExportStrategy.IncludeFile)
            settings.IncludeFilePath = paths.Prompt("Include file path", SshConfigExporter.ResolveIncludePath(settings.IncludeFilePath))
                ?? settings.IncludeFilePath;
        settings.ExportAllHosts = AnsiConsole.Confirm("Export all hosts?", settings.ExportAllHosts);
        if (!settings.ExportAllHosts)
            settings.ExportTags = ParseList(AnsiConsole.Prompt(new TextPrompt<string>("Export hosts with these tags (comma-separated):")
                .DefaultValue(string.Join(", ", settings.ExportTags)).AllowEmpty()));
        var currentExcluded = config.Hosts.Where(h => settings.ExcludeHostIds.Contains(h.Id)).Select(h => h.Alias);
        var excludedAliases = ParseList(AnsiConsole.Prompt(new TextPrompt<string>("Exclude host aliases (comma-separated, empty = none):")
            .DefaultValue(string.Join(", ", currentExcluded)).AllowEmpty()));
        var unknownAliases = excludedAliases.Where(alias => !config.Hosts.Any(h => h.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknownAliases.Count > 0) throw new InvalidOperationException("Unknown excluded aliases: " + string.Join(", ", unknownAliases));
        settings.ExcludeHostIds = config.Hosts.Where(h => excludedAliases.Contains(h.Alias, StringComparer.OrdinalIgnoreCase)).Select(h => h.Id).ToList();
        settings.ExportUser = AnsiConsole.Confirm("Export User?", settings.ExportUser);
        settings.ExportPort = AnsiConsole.Confirm("Export Port?", settings.ExportPort);
        settings.ExportIdentityFile = AnsiConsole.Confirm("Export IdentityFile?", settings.ExportIdentityFile);
        settings.ExportCustomOptions = AnsiConsole.Confirm("Export custom SSH options?", settings.ExportCustomOptions);
        settings.ImportOnStartup = AnsiConsole.Confirm("Import on application startup?", settings.ImportOnStartup);
        settings.AutoImportOnChange = AnsiConsole.Confirm("Automatically import when SSH config changes?", settings.AutoImportOnChange);
        settings.AutoExportOnChange = AnsiConsole.Confirm("Automatically export when sshocked hosts change?", settings.AutoExportOnChange);
        settings.ConflictResolution = AnsiConsole.Prompt(new SelectionPrompt<SyncConflictResolution>().Title("Default conflict policy:")
            .UseConverter(DescribeResolution).AddChoices(Enum.GetValues<SyncConflictResolution>()).DefaultValue(settings.ConflictResolution));
        repository.Update(current => current.SshConfigSync = settings);
        sync.RefreshMonitoring();
        console.WriteLine("SSH config sync settings saved.");
    }

    private void ToggleAutoSync()
    {
        repository.Update(config =>
        {
            var enable = !(config.SshConfigSync.AutoImportOnChange && config.SshConfigSync.AutoExportOnChange);
            config.SshConfigSync.AutoImportOnChange = enable;
            config.SshConfigSync.AutoExportOnChange = enable;
        });
        sync.RefreshMonitoring();
        console.WriteLine("Automatic sync setting updated. PromptUser conflicts require a manual import.");
    }

    private static List<string> ParseList(string input)
        => input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static string FormatTime(DateTimeOffset? time) => time?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "never";
    private static string DescribeResolution(SyncConflictResolution resolution) => resolution switch
    {
        SyncConflictResolution.PromptUser => "Ask for each conflict (background sync defers conflicts)",
        SyncConflictResolution.SshockedWins => "Keep sshocked version",
        SyncConflictResolution.OpenSshWins => "Use OpenSSH version",
        SyncConflictResolution.KeepBoth => "Keep both (add -openssh alias)",
        _ => "Skip conflicting host"
    };
}
