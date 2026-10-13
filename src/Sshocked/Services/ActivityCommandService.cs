using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class ActivityCommandService(IActivityService activity, AuditCommandService events,
    SessionCommandHandler recordings, IConsoleWriterService console) : IManagementCommandHandler
{
    public bool Handles(string command) => command.Equals("activity", StringComparison.OrdinalIgnoreCase);

    public Task Execute(string command, string[] arguments)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "status";
        var remaining = arguments.Length == 0 ? [] : arguments[1..];
        return action switch
        {
            "events" or "history" => events.Execute("history", remaining),
            "recordings" or "sessions" => recordings.Execute("sessions", remaining),
            "audit" => events.Execute("audit", remaining),
            "status" when remaining.Length == 0 => Status(),
            _ => throw new ArgumentException("Use activity status|events [filters]|recordings list|replay|export|delete|status|enable|disable|audit status|enable|disable|settings.")
        };
    }

    private Task Status() { console.WriteLine(activity.Status()); return Task.CompletedTask; }
}
