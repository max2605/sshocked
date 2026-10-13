using Spectre.Console;
using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class AuditConfigurationUi(IConfigRepository repository, IAuditService audit)
{
    public Task Show()
    {
        var settings = repository.Load().Audit;
        settings.Enabled = AnsiConsole.Confirm("Enable activity events?", settings.Enabled);
        settings.LogConnections = AnsiConsole.Confirm("Log connections?", settings.LogConnections);
        settings.LogCommands = AnsiConsole.Confirm("Log command and macro outcomes?", settings.LogCommands);
        settings.LogFileTransfers = AnsiConsole.Confirm("Log file transfers?", settings.LogFileTransfers);
        settings.LogConfigChanges = AnsiConsole.Confirm("Log configuration changes?", settings.LogConfigChanges);
        settings.LogCommandText = AnsiConsole.Confirm("Include redacted command text?", settings.LogCommandText);
        settings.LogFilePaths = AnsiConsole.Confirm("Include file paths?", settings.LogFilePaths);
        settings.LogSecretAccess = AnsiConsole.Confirm("Log secret access metadata (never secret values)?", settings.LogSecretAccess);
        settings.RetentionDays = AnsiConsole.Prompt(new TextPrompt<int>("Event retention days:").DefaultValue(settings.RetentionDays)
            .Validate(value => value is > 0 and <= 36500 ? ValidationResult.Success() : ValidationResult.Error("Use 1 to 36500 days.")));
        settings.MaxEntries = AnsiConsole.Prompt(new TextPrompt<int>("Maximum event count:").DefaultValue(settings.MaxEntries)
            .Validate(value => value is > 0 and <= 10000000 ? ValidationResult.Success() : ValidationResult.Error("Use 1 to 10000000 events.")));
        repository.Update(config => config.Audit = settings);
        audit.Record(new() { Category = "config", EventType = "config.settings", Action = "Activity event policy updated" });
        return Task.CompletedTask;
    }
}
