using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SessionRecordingConfigurationUi(IConfigRepository repository)
{
    public Task Show()
    {
        var settings = repository.Load().SessionRecording;
        AnsiConsole.MarkupLine("[yellow]Recording captures output, which can contain secrets. Input is never captured. Pipe recording can change terminal behavior.[/]");
        settings.Enabled = AnsiConsole.Confirm("Enable output recording by default?", settings.Enabled);
        settings.Mode = RecordingMode.OutputOnly;
        settings.CaptureInput = false;
        settings.CaptureOutput = true;
        settings.CaptureTiming = AnsiConsole.Confirm("Preserve playback timing?", settings.CaptureTiming);
        settings.Compress = AnsiConsole.Confirm("Compress completed recordings?", settings.Compress);
        settings.RetentionDays = AnsiConsole.Prompt(new TextPrompt<int>("Retention days:").DefaultValue(settings.RetentionDays)
            .Validate(value => value is > 0 and <= 36500 ? ValidationResult.Success() : ValidationResult.Error("Use 1 to 36500 days.")));
        settings.MaxTotalSizeMb = AnsiConsole.Prompt(new TextPrompt<long>("Maximum stored MiB (0 removes the limit):").DefaultValue(settings.MaxTotalSizeMb ?? 0)
            .Validate(value => value is >= 0 and <= 1000000 ? ValidationResult.Success() : ValidationResult.Error("Use 0 to 1000000 MiB.")));
        if (settings.MaxTotalSizeMb == 0) settings.MaxTotalSizeMb = null;
        repository.Update(config => config.SessionRecording = settings);
        return Task.CompletedTask;
    }

    public static bool? ConfigureHost(bool? current)
    {
        var choice = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Session output recording:")
            .AddChoices("Use global policy", "Always record", "Never record")
            .DefaultValue(current is true ? "Always record" : current is false ? "Never record" : "Use global policy"));
        return choice == "Always record" ? true : choice == "Never record" ? false : null;
    }
}
