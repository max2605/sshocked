using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ActivityService(IAuditService audit, SessionRepository sessions, IConfigRepository repository) : IActivityService
{
    public IReadOnlyList<AuditEntry> Events(AuditQuery query) => audit.Query(query);

    public IReadOnlyList<SessionMetadata> Recordings(int last = 20, string? server = null, DateTimeOffset? since = null)
    {
        sessions.ApplyRetention(repository.Load().SessionRecording);
        return sessions.List(last, server, since);
    }

    public string Status()
    {
        var config = repository.Load();
        // Events describe operations; output recordings preserve playback. They share one
        // module while separate retention policies prevent large casts from evicting events.
        return $"Activity events: {(config.Audit.Enabled ? "enabled" : "disabled")}; retention {config.Audit.RetentionDays} days / {config.Audit.MaxEntries} entries.\n" +
            $"Output recordings: {(config.SessionRecording.Enabled ? "enabled" : "disabled")}; mode {config.SessionRecording.Mode}; retention {config.SessionRecording.RetentionDays} days; directory {sessions.DirectoryPath}.\n" +
            "Events and recordings share Activity and recording. Recordings capture output only; command macros are recorded explicitly in managed command sessions.";
    }
}
