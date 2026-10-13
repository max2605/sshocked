namespace Sshocked.Models;

public sealed class MacroRecording(ServerHost host)
{
    private readonly List<string> commands = [];
    public ServerHost Host { get; } = host;
    public bool IsRecording { get; private set; }
    public IReadOnlyList<string> Commands => commands.AsReadOnly();

    public void Start()
    {
        if (IsRecording) throw new InvalidOperationException("Macro recording is already active.");
        commands.Clear();
        IsRecording = true;
    }

    public void Stop() => IsRecording = false;

    public void Capture(string command)
    {
        if (!IsRecording) return;
        Services.MacroCommandPolicy.Validate(command, Host);
        if (commands.Count >= Services.MacroCommandPolicy.MaximumCommands)
            throw new InvalidOperationException("The macro command limit has been reached. Stop and save this recording.");
        commands.Add(command);
    }
}
