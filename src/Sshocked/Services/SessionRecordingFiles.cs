using System.Text;
using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class SessionRecordingFiles
{
    public const int CastVersion = 2;
    public const string OutputEvent = "o";
    public const int BytesPerMebibyte = 1024 * 1024;
    public const int BufferSize = 4096;
    public const int DefaultWidth = 80;
    public const int DefaultHeight = 24;
    public const string CastExtension = ".cast";
    public const string MetadataExtension = ".json";
    public const string CompressionExtension = ".gz";
    public const string HeaderVersion = "version";
    public const string HeaderWidth = "width";
    public const string HeaderHeight = "height";
    public const string HeaderTimestamp = "timestamp";
    public const string HeaderTitle = "title";
    public const string HeaderRecordingType = "sshocked_recording_type";
    public const string OutputOnlyType = "output-only";
    public const string CastFormat = "cast";
    public const string TextFormat = "text";

    public static string InitializeDirectory(string? path)
    {
        var directory = Path.GetFullPath(path ?? Path.Combine(ConfigPaths.DirectoryPath, "sessions"));
        CreatePrivateDirectory(directory);
        return directory;
    }

    public static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else
        {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(path, mode);
            File.SetUnixFileMode(path, mode);
        }
    }

    public static FileStream CreatePrivateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    public static StreamWriter CreateWriter(SessionRepository repository, SessionMetadata metadata, int width, int height)
    {
        var writer = new StreamWriter(CreatePrivateFile(repository.ResolvePath(metadata.RelativeFilePath)), new UTF8Encoding(false)) { NewLine = "\n" };
        try
        {
            WriteJson(writer, json =>
            {
                json.WriteStartObject();
                json.WriteNumber(HeaderVersion, CastVersion);
                json.WriteNumber(HeaderWidth, Math.Max(width, 1));
                json.WriteNumber(HeaderHeight, Math.Max(height, 1));
                json.WriteNumber(HeaderTimestamp, metadata.StartedAt.ToUnixTimeSeconds());
                json.WriteString(HeaderTitle, $"{metadata.HostAlias} (output only)");
                json.WriteString(HeaderRecordingType, OutputOnlyType);
                json.WriteEndObject();
            });
            return writer;
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    public static void WriteJson(StreamWriter writer, Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer)) write(json);
        writer.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
        writer.Flush();
    }
}
