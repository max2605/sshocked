using System.Text;

namespace Sshocked.Services;

internal static class ConfigBackupFiles
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public static string Read(string path)
    {
        using var stream = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Configuration backups cannot exceed 16 MiB.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var buffer = new char[4096];
        var text = new StringBuilder();
        int count;
        while ((count = reader.Read(buffer)) > 0)
        {
            if (text.Length + count > MaximumBytes) throw new InvalidDataException("Configuration backups cannot exceed 16 MiB.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    public static void Write(string path, string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > MaximumBytes) throw new InvalidDataException("Configuration backups and reports cannot exceed 16 MiB.");
        path = Path.GetFullPath(path);
        if (File.Exists(path)) throw new IOException("The destination already exists. Choose a new backup file.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
