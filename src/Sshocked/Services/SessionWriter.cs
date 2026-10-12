using System.Diagnostics;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SessionWriter(SessionRepository repository, SessionMetadata metadata, bool timing, int width, int height) : IDisposable
{
    private readonly object writeGate = new();
    private readonly StreamWriter writer = SessionRecordingFiles.CreateWriter(repository, metadata, width, height);
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private bool disposed;
    public SessionMetadata Metadata => metadata;

    public void Append(string data)
    {
        lock (writeGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var elapsed = timing ? stopwatch.Elapsed.TotalSeconds : 0;
            SessionRecordingFiles.WriteJson(writer, json =>
            {
                json.WriteStartArray();
                json.WriteNumberValue(elapsed);
                json.WriteStringValue(SessionRecordingFiles.OutputEvent);
                json.WriteStringValue(data);
                json.WriteEndArray();
            });
        }
    }

    public void Complete(int? exitCode, bool compress = false)
    {
        lock (writeGate)
        {
            if (disposed) return;
            writer.Dispose();
            disposed = true;
            try { repository.Complete(metadata, exitCode, stopwatch.Elapsed.TotalSeconds, compress); }
            finally { repository.ReleaseWriter(metadata.Id); }
        }
    }

    public void Dispose()
    {
        lock (writeGate)
        {
            if (disposed) return;
            writer.Dispose();
            disposed = true;
            repository.ReleaseWriter(metadata.Id);
        }
    }
}
