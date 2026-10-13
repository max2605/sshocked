using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ActivityMenuService(IActivityService activity, SessionRepository recordings,
    SessionCommandHandler recordingCommands, AuditConfigurationUi eventPolicy, SessionRecordingConfigurationUi recordingPolicy,
    IMenuFactory menus, IConsoleWriterService console, IConsoleHelperService helper, IPathInputService paths) : IActivityMenuService
{
    public async Task Show()
    {
        var running = true;
        while (running)
        {
            console.WriteLine(activity.Status());
            running = await menus.RunMenu("[bold]Activity and recording[/]", [
                new("Browse activity timeline", 'T', executeAsync: Timeline),
                new("Browse output recordings", 'R', executeAsync: BrowseRecordings),
                new("Configure activity events", 'E', executeAsync: eventPolicy.Show),
                new("Configure output recording", 'O', executeAsync: recordingPolicy.Show),
                new("Back", 'B', actionType: MenuActionType.Back)
            ]);
        }
    }

    private Task Timeline()
    {
        var rows = activity.Events(new(Last: 30)).Select(entry => (Time: entry.Timestamp,
            Text: $"{entry.Timestamp:u} EVENT {entry.Alias ?? entry.HostId ?? "global"} {entry.EventType} {(entry.Success ? "OK" : "FAILED")} {entry.Action}"))
            .Concat(activity.Recordings(30).Select(session => (Time: session.StartedAt,
                Text: $"{session.StartedAt:u} RECORDING {session.HostAlias} {session.Id} {session.DurationSeconds:0.#}s {session.FileSize} bytes")))
            .OrderByDescending(row => row.Time).Take(30).ToList();
        foreach (var row in rows) console.WriteLine(row.Text);
        if (rows.Count == 0) console.WriteLine("No activity recorded yet.");
        helper.WaitForKey();
        return Task.CompletedTask;
    }

    private async Task BrowseRecordings()
    {
        var sessions = activity.Recordings(100);
        if (sessions.Count == 0) { console.WriteLine("No output recordings found. Recording is disabled by default."); helper.WaitForKey(); return; }
        var choices = sessions.ToDictionary(session => $"{session.StartedAt:u} {session.HostAlias} ({session.Id})");
        var selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Output recordings:").UseConverter(Markup.Escape).AddChoices(choices.Keys.Append("Back")));
        if (!choices.TryGetValue(selected, out var session)) return;
        var running = true;
        while (running)
        {
            running = await menus.RunMenu($"[bold]Recording: {Markup.Escape(session.HostAlias)}[/]", [
                new("Replay output", 'R', executeAsync: () => recordingCommands.Execute("sessions", ["replay", session.Id])),
                new("Export recording", 'E', executeAsync: () => Export(session)),
                new("Delete recording", 'D', executeAsync: () =>
                {
                    if (AnsiConsole.Confirm("Permanently delete this output recording?", false)) { recordings.Delete(session.Id); running = false; }
                    return Task.CompletedTask;
                }),
                new("Back", 'B', actionType: MenuActionType.Back)
            ]) && running;
        }
    }

    private Task Export(SessionMetadata session)
    {
        var format = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Export format:").AddChoices("cast", "text", "Back"));
        if (format == "Back") return Task.CompletedTask;
        var path = paths.Prompt("Export path", Path.Combine(Environment.CurrentDirectory, $"{session.Id}.{(format == "text" ? "txt" : "cast")}"));
        if (path is not null) console.WriteLine(recordings.Export(session.Id, path, format));
        helper.WaitForKey();
        return Task.CompletedTask;
    }
}
