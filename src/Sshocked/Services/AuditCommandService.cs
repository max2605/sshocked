using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class AuditCommandService(IAuditService audit, IConfigRepository repository, IConsoleWriterService console) : IManagementCommandHandler
{
    public bool Handles(string command) => command is "history" or "audit";

    public Task Execute(string command, string[] arguments)
    {
        if (command == "history") History(arguments); else Settings(arguments);
        return Task.CompletedTask;
    }

    private void History(string[] arguments)
    {
        var options = new CommandOptions(arguments, ["last", "type", "server", "since", "until", "output"], ["clear", "force"]);
        if (options.Positionals.Count > 0) throw new ArgumentException("Usage: ssk history [--last N] [--type category] [--server alias] [--since date] [--until date] [--output table|json|jsonl|csv]");
        if (options.Has("clear"))
        {
            if (!options.Has("force"))
            {
                if (Console.IsInputRedirected) throw new ArgumentException("Use history --clear --force to clear audit history without a terminal.");
                if (!AnsiConsole.Confirm("Permanently clear audit history?", false)) return;
            }
            audit.Clear();
            console.WriteLine("Audit history cleared.");
            return;
        }
        var since = options.Date("since");
        var until = options.Date("until");
        if (since > until) throw new ArgumentException("--since must precede --until.");
        var entries = audit.Query(new(options.Integer("last", 20), options.Get("type"), options.Get("server"), since, until));
        switch (options.Get("output", "table")!.ToLowerInvariant())
        {
            case "json": case "pretty-json":
                console.WriteLine(JsonSerializer.Serialize(entries.ToList(), AuditJsonContext.Default.ListAuditEntry)); break;
            case "jsonl":
                foreach (var entry in entries) console.WriteLine(JsonSerializer.Serialize(entry, AuditJsonContext.Default.AuditEntry)); break;
            case "csv":
                console.WriteLine("timestamp,eventType,server,action,success,exitCode");
                foreach (var entry in entries) console.WriteLine(string.Join(',',
                    Csv(entry.Timestamp.ToString("O")), Csv(entry.EventType), Csv(entry.Alias ?? entry.HostId), Csv(entry.Action),
                    entry.Success.ToString().ToLowerInvariant(), entry.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "")); break;
            case "table":
                foreach (var entry in entries) console.WriteLine($"{entry.Timestamp:u} {entry.EventType,-20} {entry.Alias ?? entry.HostId,-20} {(entry.Success ? "OK" : "FAILED")} {entry.Action}");
                if (entries.Count == 0) console.WriteLine("No matching audit entries."); break;
            default: throw new ArgumentException("--output must be table, json, pretty-json, jsonl or csv.");
        }
    }

    private void Settings(string[] arguments)
    {
        var options = new CommandOptions(arguments, ["retention-days", "max-entries", "command-text", "file-paths", "secret-access"]);
        var action = options.Positionals.SingleOrDefault() ?? "status";
        if (action == "status")
        {
            var settings = repository.Load().Audit;
            console.WriteLine($"Audit: {(settings.Enabled ? "enabled" : "disabled")}; retention {settings.RetentionDays} days / {settings.MaxEntries} entries.");
            return;
        }
        if (action is not ("enable" or "disable" or "settings")) throw new ArgumentException("Usage: ssk audit status|enable|disable|settings [--retention-days N] [--max-entries N] [--command-text true|false] [--file-paths true|false] [--secret-access true|false]");
        var retention = options.Integer("retention-days", repository.Load().Audit.RetentionDays, 1, 36500);
        var count = options.Integer("max-entries", repository.Load().Audit.MaxEntries, 1, 10000000);
        bool? ReadBool(string name) => options.Get(name) is not { } raw ? null : bool.TryParse(raw, out var value) ? value : throw new ArgumentException($"--{name} must be true or false.");
        var commandText = ReadBool("command-text"); var filePaths = ReadBool("file-paths"); var secrets = ReadBool("secret-access");
        repository.Update(c =>
        {
            if (action == "enable") c.Audit.Enabled = true;
            if (action == "disable") c.Audit.Enabled = false;
            c.Audit.RetentionDays = retention; c.Audit.MaxEntries = count;
            if (commandText.HasValue) c.Audit.LogCommandText = commandText.Value;
            if (filePaths.HasValue) c.Audit.LogFilePaths = filePaths.Value;
            if (secrets.HasValue) c.Audit.LogSecretAccess = secrets.Value;
        });
        audit.Record(new() { Category = "config", EventType = "config.settings", Action = "audit " + action });
        console.WriteLine("Audit settings updated.");
    }

    private static string Csv(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
