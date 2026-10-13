using System.Diagnostics;
using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ForwardingRuntimeStore(string? directory = null)
{
    private readonly string _directory = Path.GetFullPath(directory ?? Path.Combine(ConfigPaths.DirectoryPath, "forwarding"));

    public FileStream Acquire(string id)
    {
        EnsureDirectory();
        try { return new FileStream(PathFor(id, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("This forwarding profile is already owned by another running operation."); }
    }

    public ForwardingRuntimeRecord? Read(string id)
    {
        var path = PathFor(id, ".json");
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Forwarding runtime record is too large.");
            return JsonSerializer.Deserialize(File.ReadAllText(path), ForwardingJsonContext.Default.ForwardingRuntimeRecord);
        }
        catch (FileNotFoundException) { return null; }
    }

    public void Write(ForwardingRuntimeRecord record)
    {
        EnsureDirectory();
        WriteText(PathFor(record.Status.ProfileId, ".json"), JsonSerializer.Serialize(record, ForwardingJsonContext.Default.ForwardingRuntimeRecord));
    }

    public void RequestStop(ForwardingRuntimeRecord record)
    {
        EnsureDirectory();
        WriteText(PathFor(record.Status.ProfileId, ".stop"), record.ControlToken);
    }

    public async Task WaitForStop(string id, string token, CancellationToken ct)
    {
        var path = PathFor(id, ".stop");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                try { if (new FileInfo(path).Length < 128 && File.ReadAllText(path) == token) return; }
                catch (FileNotFoundException) { }
            }
            await Task.Delay(200, ct);
        }
    }

    public void ClearStop(string id)
    {
        var path = PathFor(id, ".stop");
        if (File.Exists(path)) File.Delete(path);
    }

    public static bool OwnerAlive(ForwardingStatus status)
    {
        try
        {
            using var owner = Process.GetProcessById(status.OwnerProcessId);
            return !owner.HasExited && owner.StartTime.ToUniversalTime() == status.OwnerStartedUtc.UtcDateTime;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public static (int Id, DateTimeOffset StartedUtc) CurrentOwner()
    {
        using var process = Process.GetCurrentProcess();
        return (process.Id, new(process.StartTime.ToUniversalTime()));
    }

    private string PathFor(string id, string extension)
    {
        ForwardingValidation.ValidateId(id);
        return Path.Combine(_directory, id + extension);
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(_directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void WriteText(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
