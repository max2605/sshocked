using System.Globalization;
using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SessionCommandHandler(SessionRepository repository, IConfigRepository configRepository) : IManagementCommandHandler
{
    public bool Handles(string command) => command.Equals(SessionCommandNames.Command, StringComparison.OrdinalIgnoreCase);

    public async Task Execute(string command, string[] arguments)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? SessionCommandNames.List;
        var remaining = arguments.Length > 0 ? arguments[1..] : [];
        switch (action)
        {
            case SessionCommandNames.List:
                {
                    var options = new CommandOptions(remaining, [SessionCommandNames.LastOption, SessionCommandNames.ServerOption, SessionCommandNames.SinceOption]);
                    if (options.Positionals.Count != 0) throw new ArgumentException("Use sessions list --last N --server ALIAS --since DATE.");
                    repository.ApplyRetention(configRepository.Load().SessionRecording);
                    var sessions = repository.List(options.Integer(SessionCommandNames.LastOption, 20), options.Get(SessionCommandNames.ServerOption), options.Date(SessionCommandNames.SinceOption));
                    var table = new Table().AddColumn("Session ID").AddColumn("Server").AddColumn("Started (UTC)")
                        .AddColumn("Seconds").AddColumn("Bytes").AddColumn("Type");
                    foreach (var session in sessions)
                        table.AddRow(session.Id, Markup.Escape(session.HostAlias), session.StartedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            session.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture), session.FileSize.ToString(CultureInfo.InvariantCulture), Markup.Escape(session.RecordingType));
                    AnsiConsole.Write(table);
                    if (sessions.Count == 0) System.Console.WriteLine("No recordings found. Recording is disabled by default.");
                    break;
                }
            case SessionCommandNames.Replay:
                {
                    var options = new CommandOptions(remaining, [SessionCommandNames.SpeedOption], [SessionCommandNames.NoControlsOption]);
                    var id = SingleId(options);
                    var speedText = options.Get(SessionCommandNames.SpeedOption, "1")!;
                    if (!double.TryParse(speedText, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
                        throw new ArgumentException("--speed must be a number from 0.1 to 16.");
                    var session = repository.Find(id);
                    using var cancellation = new CancellationTokenSource();
                    ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
                    System.Console.CancelKeyPress += cancel;
                    try
                    {
                        await SessionReplayService.Replay(repository.ReadEvents(session), speed, interactive:
                            !options.Has(SessionCommandNames.NoControlsOption) && !System.Console.IsInputRedirected && !System.Console.IsOutputRedirected,
                            cancellationToken: cancellation.Token);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                    finally { System.Console.CancelKeyPress -= cancel; }
                    break;
                }
            case SessionCommandNames.Export:
                {
                    var options = new CommandOptions(remaining, [SessionCommandNames.OutputOption, SessionCommandNames.FormatOption]);
                    var id = SingleId(options);
                    var format = options.Get(SessionCommandNames.FormatOption, SessionRecordingFiles.CastFormat)!.ToLowerInvariant();
                    var output = options.Get(SessionCommandNames.OutputOption, Path.Combine(Environment.CurrentDirectory, $"{id}.{(format == SessionRecordingFiles.TextFormat ? "txt" : SessionRecordingFiles.CastFormat)}"))!;
                    System.Console.WriteLine(repository.Export(id, output, format));
                    break;
                }
            case SessionCommandNames.Delete:
                {
                    var options = new CommandOptions(remaining, [SessionCommandNames.OlderOption]);
                    if (options.Has(SessionCommandNames.OlderOption))
                    {
                        if (options.Positionals.Count != 0) throw new ArgumentException("Use a session ID or --older, not both.");
                        var raw = options.Get(SessionCommandNames.OlderOption)!;
                        if (raw.EndsWith('d')) raw = raw[..^1];
                        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days is < 1 or > 365000)
                            throw new ArgumentException("--older must be a positive day count, for example 30d.");
                        var count = repository.DeleteOlderThan(DateTimeOffset.UtcNow.AddDays(-days));
                        System.Console.WriteLine($"Deleted {count} recording(s).");
                    }
                    else { repository.Delete(SingleId(options)); System.Console.WriteLine("Recording deleted."); }
                    break;
                }
            case SessionCommandNames.Status:
                {
                    if (remaining.Length != 0) throw new ArgumentException("sessions status takes no options.");
                    var settings = configRepository.Load().SessionRecording;
                    System.Console.WriteLine($"Recording: {(settings.Enabled ? "enabled" : "disabled")}; mode: {settings.Mode}; output only; input capture unavailable.");
                    System.Console.WriteLine($"Retention: {settings.RetentionDays} days; size limit: {settings.MaxTotalSizeMb?.ToString() ?? "none"} MiB; directory: {repository.DirectoryPath}");
                    break;
                }
            case SessionCommandNames.Enable:
            case SessionCommandNames.Disable:
                {
                    if (remaining.Length != 0) throw new ArgumentException($"sessions {action} takes no options.");
                    configRepository.Update(config =>
                    {
                        config.SessionRecording.Enabled = action == SessionCommandNames.Enable;
                        if (action == SessionCommandNames.Enable && config.SessionRecording.Mode == RecordingMode.Disabled)
                            config.SessionRecording.Mode = RecordingMode.OutputOnly;
                    });
                    System.Console.WriteLine(action == SessionCommandNames.Enable
                        ? "Output-only recording enabled. Output can contain sensitive data; stdin is never captured."
                        : "Session recording disabled globally. Explicit host overrides still apply.");
                    break;
                }
            default: throw new ArgumentException("Use sessions list|replay|export|delete|status|enable|disable.");
        }
    }

    private static string SingleId(CommandOptions options)
    {
        if (options.Positionals.Count != 1) throw new ArgumentException("Exactly one session ID is required.");
        return options.Positionals[0];
    }
}
