namespace Sshocked.Models;

public sealed class RemoteFileEntry
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public string Permissions { get; set; } = string.Empty;
    public DateTime? LastModified { get; set; }
}
