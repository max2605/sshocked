using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ManagedCommandSessionService(IRemoteCommandSessionService sessions, IMacroService macros,
    IConsoleWriterService console) : IManagedCommandSessionService
{
    public Task Show(ServerHost host) => Show(host, CancellationToken.None);

    public async Task Show(ServerHost host, CancellationToken cancellationToken)
    {
        console.WriteLine($"Command session: {host.Alias}. POSIX shell; cd and exported variables persist.");
        console.WriteLine("Controls: :record, :stop, :save NAME, :discard, :exit. Command output is streamed live.");
        console.WriteLine("Recording captures only commands entered here. Macros store command text in the local configuration; keep secrets in remote credential references.");
        console.WriteLine("Interactive programs and password prompts use Connect. Command stdin is closed; SSH credentials are never recorded.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        var recording = new MacroRecording(host);
        try
        {
            await using var session = await sessions.Open(host, cancellation.Token);
            while (!cancellation.IsCancellationRequested)
            {
                console.Write($"{host.Alias}{(recording.IsRecording ? " [REC]" : "")}> ");
                var command = console.ReadLine();
                if (command is null || command.Trim().Equals(":exit", StringComparison.OrdinalIgnoreCase)) break;
                if (string.IsNullOrWhiteSpace(command)) continue;
                try
                {
                    if (Control(command.Trim(), recording, out var reset))
                    {
                        if (reset) recording = new MacroRecording(host);
                        continue;
                    }
                    if (recording.IsRecording) session.ValidateRecordingCommand(command);
                    recording.Capture(command);
                    var result = await session.Execute(command, console.Write, cancellation.Token);
                    console.WriteLine($"\nExit code: {result.ExitCode}");
                }
                catch (ArgumentException exception) { console.WriteLine(exception.Message + " Command was not executed."); }
                catch (InvalidOperationException exception) { console.WriteLine(exception.Message); }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { console.WriteLine("Command session cancelled."); }
        finally
        {
            Console.CancelKeyPress -= cancel;
            if (recording.Commands.Count > 0) console.WriteLine($"Discarded unsaved draft ({recording.Commands.Count} commands).");
        }
    }

    private bool Control(string command, MacroRecording recording, out bool reset)
    {
        reset = false;
        if (command.Equals(":record", StringComparison.OrdinalIgnoreCase) || command.Equals(":record start", StringComparison.OrdinalIgnoreCase))
        {
            if (recording.Commands.Count > 0) throw new InvalidOperationException("Save or discard the current draft before starting another recording.");
            recording.Start(); console.WriteLine("Macro recording started."); return true;
        }
        if (command.Equals(":stop", StringComparison.OrdinalIgnoreCase) || command.Equals(":record stop", StringComparison.OrdinalIgnoreCase))
        { recording.Stop(); console.WriteLine($"Macro recording stopped ({recording.Commands.Count} commands). Use :save NAME."); return true; }
        if (command.StartsWith(":save ", StringComparison.OrdinalIgnoreCase))
        {
            var macro = macros.Save(recording, command[6..]);
            console.WriteLine($"Saved macro '{macro.Name}' ({macro.Commands.Count} commands)."); reset = true; return true;
        }
        if (command.Equals(":save", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use :save NAME after stopping the recording.");
        if (command.Equals(":discard", StringComparison.OrdinalIgnoreCase))
        { recording.Stop(); console.WriteLine("Macro draft discarded."); reset = true; return true; }
        return false;
    }
}
