using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IFileTransferService
{
    Task<int> UploadFile(ServerHost host, string localPath, string remotePath,
        bool recursive = false, CancellationToken cancellationToken = default);
    Task<int> DownloadFile(ServerHost host, string remotePath, string localPath,
        bool recursive = false, CancellationToken cancellationToken = default);
    Task<List<RemoteFileEntry>> ListRemoteDirectory(ServerHost host, string remotePath,
        CancellationToken cancellationToken = default);
    Task<List<FileTransferResult>> UploadToGroup(IEnumerable<ServerHost> hosts, string localPath,
        string remoteBasePath, bool recursive = false, CancellationToken cancellationToken = default);
    Task<List<FileTransferResult>> DownloadFromGroup(IEnumerable<ServerHost> hosts, string remotePath,
        string localBaseDir, bool recursive = false, CancellationToken cancellationToken = default);
}
