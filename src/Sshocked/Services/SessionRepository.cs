using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SessionRepository
{
    private readonly object gate = new();
    private readonly HashSet<string> active = [];
    public string DirectoryPath { get; }

    public SessionRepository() : this(DefaultDirectory()) { }

    public SessionRepository(string directoryPath)
    {
        DirectoryPath = Path.GetFullPath(directoryPath);
        CreatePrivateDirectory(DirectoryPath);
    }

    private static string DefaultDirectory() => Path.Combine(ConfigPaths.DirectoryPath, "sessions");

    public SessionWriter Create(ServerHost host, bool captureTiming = true, int width = 80, int height = 24)
    {
        var metadata = new SessionMetadata
        {
            HostId = host.Id, HostAlias = host.Alias, HostName = host.HostName, User = host.User
        };
        var dateDirectory = Path.Combine(DirectoryPath, metadata.StartedAt.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture));
        CreatePrivateDirectory(dateDirectory);
        var alias = string.Concat(host.Alias.Take(60).Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var filename = $"{metadata.StartedAt:yyyyMMddTHHmmssfffZ}_{alias}_{metadata.Id[..8]}.cast";
        metadata.RelativeFilePath = Path.GetRelativePath(DirectoryPath, Path.Combine(dateDirectory, filename));
        lock (gate) active.Add(metadata.Id);
        SessionWriter? createdWriter = null;
        try
        {
            createdWriter = new SessionWriter(this, metadata, captureTiming, width, height);
            Save(metadata);
            return createdWriter;
        }
        catch { createdWriter?.Dispose(); lock (gate) active.Remove(metadata.Id); throw; }
    }

    public IReadOnlyList<SessionMetadata> List(int last = 20, string? server = null, DateTimeOffset? since = null)
    {
        if (last < 1) throw new ArgumentException("The session count must be positive.");
        var sessions = new List<SessionMetadata>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json", options))
        {
            try
            {
                if (new FileInfo(path).Length > 1024 * 1024) continue;
                var metadata = JsonSerializer.Deserialize(File.ReadAllText(path), SessionRecordingJsonContext.Default.SessionMetadata);
                if (metadata is null || !Guid.TryParse(metadata.Id, out _) || string.IsNullOrWhiteSpace(metadata.RelativeFilePath)) continue;
                if (metadata.HostId is null || metadata.HostAlias is null || metadata.HostName is null || metadata.User is null ||
                    metadata.RecordingType is null || !double.IsFinite(metadata.DurationSeconds) || metadata.DurationSeconds < 0) continue;
                var recordingPath = ResolvePath(metadata.RelativeFilePath);
                if (!File.Exists(recordingPath) || Path.GetFullPath(path) != Path.ChangeExtension(recordingPath.EndsWith(".gz", StringComparison.Ordinal) ? recordingPath[..^3] : recordingPath, ".json")) continue;
                if (server is not null && !metadata.HostId.Equals(server, StringComparison.OrdinalIgnoreCase) && !metadata.HostAlias.Equals(server, StringComparison.OrdinalIgnoreCase)) continue;
                if (since is not null && metadata.StartedAt < since) continue;
                metadata.FileSize = new FileInfo(recordingPath).Length;
                sessions.Add(metadata);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        return sessions.OrderByDescending(session => session.StartedAt).Take(last).ToList();
    }

    public SessionMetadata Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length < 6 || !id.All(Uri.IsHexDigit))
            throw new ArgumentException("Use the session ID, or an unambiguous hexadecimal prefix of at least six characters.");
        var matches = List(int.MaxValue).Where(session => session.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0], 0 => throw new FileNotFoundException($"Session '{id}' was not found."),
            _ => throw new ArgumentException($"Session prefix '{id}' matches more than one recording.")
        };
    }

    public IEnumerable<SessionEvent> ReadEvents(SessionMetadata metadata)
    {
        using var file = File.OpenRead(ResolvePath(metadata.RelativeFilePath));
        using var content = metadata.Compressed ? new GZipStream(file, CompressionMode.Decompress, leaveOpen: true) : null;
        using var reader = new StreamReader(content ?? (Stream)file, Encoding.UTF8);
        var header = reader.ReadLine() ?? throw new InvalidDataException("The recording header is missing.");
        using (var document = JsonDocument.Parse(header))
            if (!document.RootElement.TryGetProperty("version", out var version) || version.GetInt32() != 2)
                throw new InvalidDataException("Only asciinema cast version 2 is supported.");
        var previous = 0d;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            var entry = document.RootElement;
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() != 3 || entry[1].GetString() != "o")
                throw new InvalidDataException("The recording contains an unsupported event.");
            var timestamp = entry[0].GetDouble();
            if (!double.IsFinite(timestamp) || timestamp < previous)
                throw new InvalidDataException("Recording times must be finite and chronological.");
            previous = timestamp;
            yield return new(timestamp, entry[2].GetString() ?? string.Empty);
        }
    }

    public string Export(string id, string outputPath, string format = "cast")
    {
        var metadata = Find(id);
        if (format is not ("cast" or "text")) throw new ArgumentException("Export format must be cast or text.");
        var destination = Path.GetFullPath(outputPath);
        using var output = CreatePrivateFile(destination);
        if (format == "text")
        {
            using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
            foreach (var entry in ReadEvents(metadata)) writer.Write(entry.Data);
        }
        else
        {
            using var input = File.OpenRead(ResolvePath(metadata.RelativeFilePath));
            if (metadata.Compressed)
            { using var decompressed = new GZipStream(input, CompressionMode.Decompress); decompressed.CopyTo(output); }
            else input.CopyTo(output);
        }
        return destination;
    }

    public void Delete(string id) => Delete(Find(id));

    private void Delete(SessionMetadata metadata)
    {
        lock (gate)
        {
            if (active.Contains(metadata.Id)) throw new InvalidOperationException("An active recording cannot be deleted.");
            var path = ResolvePath(metadata.RelativeFilePath);
            File.Delete(path);
            File.Delete(MetadataPath(metadata));
        }
    }

    public int DeleteOlderThan(DateTimeOffset cutoff, bool completedOnly = false)
    {
        var count = 0;
        foreach (var metadata in List(int.MaxValue).Where(session => session.StartedAt < cutoff && (!completedOnly || session.EndedAt is not null)))
        {
            lock (gate) if (active.Contains(metadata.Id)) continue;
            Delete(metadata);
            count++;
        }
        return count;
    }

    public void ApplyRetention(SessionRecordingSettings settings)
    {
        if (settings.RetentionDays is < 0 or > 365000) throw new ArgumentException("Session retention days must be between 0 and 365000.");
        if (settings.RetentionDays > 0) DeleteOlderThan(DateTimeOffset.UtcNow.AddDays(-settings.RetentionDays), completedOnly: true);
        if (settings.MaxTotalSizeMb is not { } megabytes) return;
        if (megabytes < 1 || megabytes > long.MaxValue / (1024 * 1024)) throw new ArgumentException("The recording size limit must be positive.");
        // An incomplete recording may belong to another sshocked process, whose active
        // set is not visible here. Automatic cleanup must preserve all incomplete casts.
        var sessions = List(int.MaxValue).Where(session => session.EndedAt is not null).OrderBy(session => session.StartedAt).ToList();
        var total = sessions.Sum(session => new FileInfo(ResolvePath(session.RelativeFilePath)).Length);
        var limit = megabytes * 1024 * 1024;
        foreach (var metadata in sessions)
        {
            if (total <= limit) break;
            lock (gate) if (active.Contains(metadata.Id)) continue;
            var size = new FileInfo(ResolvePath(metadata.RelativeFilePath)).Length;
            Delete(metadata);
            total -= size;
        }
    }

    private void Complete(SessionMetadata metadata, int? exitCode, double duration, bool compress)
    {
        metadata.EndedAt = DateTimeOffset.UtcNow;
        metadata.DurationSeconds = duration;
        metadata.ExitCode = exitCode;
        var path = ResolvePath(metadata.RelativeFilePath);
        if (compress)
        {
            var compressedPath = path + ".gz";
            var originalRelativePath = metadata.RelativeFilePath;
            var createdCompressedFile = false;
            try
            {
                using (var input = File.OpenRead(path))
                {
                    using var output = CreatePrivateFile(compressedPath);
                    createdCompressedFile = true;
                    using var gzip = new GZipStream(output, CompressionLevel.Optimal);
                    input.CopyTo(gzip);
                }
                metadata.Compressed = true;
                metadata.RelativeFilePath += ".gz";
                metadata.FileSize = new FileInfo(compressedPath).Length;
                Save(metadata);
            }
            catch
            {
                metadata.Compressed = false;
                metadata.RelativeFilePath = originalRelativePath;
                if (createdCompressedFile) File.Delete(compressedPath);
                throw;
            }
            // Metadata already points at the complete compressed file. An undeletable original
            // is an orphan rather than a reason to remove the only indexed recording.
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        else
        {
            metadata.FileSize = new FileInfo(path).Length;
            Save(metadata);
        }
        lock (gate) active.Remove(metadata.Id);
    }

    private void Save(SessionMetadata metadata)
    {
        var path = MetadataPath(metadata);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = CreatePrivateFile(temporary))
                JsonSerializer.Serialize(stream, metadata, SessionRecordingJsonContext.Default.SessionMetadata);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string MetadataPath(SessionMetadata metadata)
    {
        var path = ResolvePath(metadata.RelativeFilePath);
        return Path.ChangeExtension(metadata.Compressed ? path[..^3] : path, ".json");
    }

    public string ResolvePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("Recording paths must be relative.");
        var path = Path.GetFullPath(Path.Combine(DirectoryPath, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(DirectoryPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("The recording path leaves the sessions directory.");
        return path;
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else
        {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(path, mode);
            File.SetUnixFileMode(path, mode);
        }
    }

    private static FileStream CreatePrivateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    public sealed class SessionWriter : IDisposable
    {
        private readonly object writeGate = new();
        private readonly SessionRepository repository;
        private readonly StreamWriter writer;
        private readonly bool timing;
        private readonly System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private bool disposed;
        public SessionMetadata Metadata { get; }

        internal SessionWriter(SessionRepository repository, SessionMetadata metadata, bool timing, int width, int height)
        {
            this.repository = repository;
            Metadata = metadata;
            this.timing = timing;
            writer = new StreamWriter(CreatePrivateFile(repository.ResolvePath(metadata.RelativeFilePath)), new UTF8Encoding(false)) { NewLine = "\n" };
            WriteJson(json =>
            {
                json.WriteStartObject(); json.WriteNumber("version", 2); json.WriteNumber("width", Math.Max(width, 1));
                json.WriteNumber("height", Math.Max(height, 1)); json.WriteNumber("timestamp", metadata.StartedAt.ToUnixTimeSeconds());
                json.WriteString("title", $"{metadata.HostAlias} (output only)"); json.WriteString("sshocked_recording_type", "output-only");
                json.WriteEndObject();
            });
        }

        public void Append(string data)
        {
            lock (writeGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                var elapsed = timing ? stopwatch.Elapsed.TotalSeconds : 0;
                WriteJson(json => { json.WriteStartArray(); json.WriteNumberValue(elapsed); json.WriteStringValue("o"); json.WriteStringValue(data); json.WriteEndArray(); });
            }
        }

        private void WriteJson(Action<Utf8JsonWriter> write)
        {
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer)) write(json);
            writer.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
            writer.Flush();
        }

        public void Complete(int? exitCode, bool compress = false)
        {
            lock (writeGate)
            {
                if (disposed) return;
                writer.Dispose();
                disposed = true;
                try { repository.Complete(Metadata, exitCode, stopwatch.Elapsed.TotalSeconds, compress); }
                finally { lock (repository.gate) repository.active.Remove(Metadata.Id); }
            }
        }

        public void Dispose()
        {
            lock (writeGate)
            {
                if (disposed) return;
                writer.Dispose(); disposed = true;
                lock (repository.gate) repository.active.Remove(Metadata.Id);
            }
        }
    }
}
