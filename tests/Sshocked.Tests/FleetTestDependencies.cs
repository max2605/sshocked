using System.Collections.Concurrent;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class FleetTestDependencies : IConfigRepository, IAuditService, IProcessService,
    IMacroService, IFileTransferService, IConsoleWriterService
{
    public AppConfig Config { get; } = new();
    public ConcurrentBag<(string HostId, string HostName, string Operation, string Payload)> Calls { get; } = [];
    public List<AuditEntry> Events { get; } = [];
    public Queue<string?> Input { get; } = new();
    public List<string> Output { get; } = [];
    public string RemoteOutput { get; set; } = "private output";
    public string RemoteError { get; set; } = "private error";
    public Func<ServerHost, CancellationToken, Task<int>> Work { get; set; } = (_, ct) =>
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(0); };
    public AppConfig Load() => Config;
    public void Save(AppConfig config) => throw new NotSupportedException();
    public void Update(Action<AppConfig> action) => action(Config);
    public void Record(AuditEntry entry) => Events.Add(entry);
    public IReadOnlyList<AuditEntry> Query(AuditQuery query) => Events;
    public void Clear() => Events.Clear();
    public Task RunSsh(ServerHost host) => Task.CompletedTask;
    public Task RunSshInteractive(ServerHost host, string command) => Task.CompletedTask;
    public Task<string> RunSshCommand(ServerHost host, string command) => Task.FromResult("");
    public async Task<ProcessResult> RunSshCommandResult(ServerHost host, string command, CancellationToken ct = default)
    {
        Calls.Add((host.Id, host.HostName, "command", command));
        return new(await Work(host, ct), RemoteOutput, RemoteError);
    }
    public Task<ProcessResult> RunSshCommandResultNonInteractive(ServerHost host, string command, CancellationToken ct = default) =>
        RunSshCommandResult(host, command, ct);
    public IReadOnlyList<MacroDefinition> List() => Config.Macros;
    public MacroDefinition Find(string nameOrId) => Config.Macros.Single(macro => macro.Name == nameOrId || macro.Id == nameOrId);
    public MacroDefinition Save(MacroRecording recording, string name) => throw new NotSupportedException();
    public void Delete(string id) => throw new NotSupportedException();
    public async Task<IReadOnlyList<MacroTargetResult>> Run(MacroDefinition macro, IReadOnlyList<ServerHost> targets,
        Action<string> progress, CancellationToken cancellationToken = default)
    {
        var results = new List<MacroTargetResult>();
        foreach (var host in targets)
        {
            Calls.Add((host.Id, host.HostName, "macro", string.Join("\n", macro.Commands)));
            var exit = await Work(host, cancellationToken);
            progress(RemoteOutput);
            progress(RemoteError);
            results.Add(new(host.Id, host.Alias, exit == 0, exit, null));
        }
        return results;
    }
    public Task<IReadOnlyList<MacroTargetResult>> RunNonInteractive(MacroDefinition macro, IReadOnlyList<ServerHost> targets,
        Action<string> progress, CancellationToken cancellationToken = default) => Run(macro, targets, progress, cancellationToken);
    public Task<int> UploadFile(ServerHost host, string localPath, string remotePath, bool recursive = false, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Batch transfers must be noninteractive.");
    public Task<int> DownloadFile(ServerHost host, string remotePath, string localPath, bool recursive = false, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Batch transfers must be noninteractive.");
    public Task<int> UploadFileNonInteractive(ServerHost host, string localPath, string remotePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        Calls.Add((host.Id, host.HostName, "upload", localPath));
        return Work(host, cancellationToken);
    }
    public Task<int> DownloadFileNonInteractive(ServerHost host, string remotePath, string localPath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        Calls.Add((host.Id, host.HostName, "download", localPath));
        return Work(host, cancellationToken);
    }
    public Task<List<RemoteFileEntry>> ListRemoteDirectory(ServerHost host, string remotePath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<List<FileTransferResult>> UploadToGroup(IEnumerable<ServerHost> hosts, string localPath, string remoteBasePath, bool recursive = false, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<List<FileTransferResult>> DownloadFromGroup(IEnumerable<ServerHost> hosts, string remotePath, string localBaseDir, bool recursive = false, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public void WriteLine(string message) => Output.Add(message);
    public void Write(string message) => Output.Add(message);
    public string? ReadLine() => Input.Count == 0 ? null : Input.Dequeue();
}
