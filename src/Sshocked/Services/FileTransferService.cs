using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class FileTransferService(IProcessService processService, IExternalProcessRunner processRunner,
    IAuditService auditService, ILogger<FileTransferService> logger) : IFileTransferService
{
    public Task<int> UploadFile(ServerHost host, string localPath, string remotePath, bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        localPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(localPath));
        if (!File.Exists(localPath) && !(recursive && Directory.Exists(localPath)))
            throw new FileNotFoundException("The local file does not exist, or copying its directory needs recursive mode.", localPath);
        return Transfer(host, localPath, remotePath, true, recursive, cancellationToken);
    }

    public Task<int> DownloadFile(ServerHost host, string remotePath, string localPath, bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        localPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(localPath));
        var parent = Path.GetDirectoryName(localPath);
        if (parent is not null && !Directory.Exists(parent))
            throw new DirectoryNotFoundException($"Local destination directory does not exist: {parent}");
        return Transfer(host, localPath, remotePath, false, recursive, cancellationToken);
    }

    private async Task<int> Transfer(ServerHost host, string localPath, string remotePath, bool upload,
        bool recursive, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        int? exitCode = null;
        try
        {
            await using var lease = await processService.PrepareConnectionAsync(host, cancellationToken);
            var startInfo = new ProcessStartInfo("scp") { UseShellExecute = false };
            foreach (var argument in ScpArguments.Build(lease.Host, localPath, remotePath, upload, recursive))
                startInfo.ArgumentList.Add(argument);
            foreach (var variable in lease.Environment)
                startInfo.Environment[variable.Key] = variable.Value;
            // Inherit the terminal for SCP progress, host-key confirmation and password/agent prompts.
            var result = await processRunner.RunAsync(startInfo, cancellationToken);
            exitCode = result.ExitCode;
            logger.LogInformation("SCP {Direction} for {Alias} finished with exit code {ExitCode}",
                upload ? "upload" : "download", host.Alias, exitCode);
            return result.ExitCode;
        }
        finally
        {
            auditService.Record(new AuditEntry
            {
                EventType = upload ? "file-upload" : "file-download", Category = "filetransfer", HostId = host.Id, Alias = host.Alias,
                Action = upload ? $"{localPath} -> {remotePath}" : $"{remotePath} -> {localPath}", Success = exitCode == 0, ExitCode = exitCode,
                DurationSeconds = stopwatch.Elapsed.TotalSeconds
            });
        }
    }

    public async Task<List<RemoteFileEntry>> ListRemoteDirectory(ServerHost host, string remotePath,
        CancellationToken cancellationToken = default)
    {
        ScpArguments.ValidatePath(remotePath);
        await using var lease = await processService.PrepareConnectionAsync(host, cancellationToken);
        var command = $"LC_ALL=C ls -lan -- {RemoteShell.QuotePath(remotePath)}";
        var startInfo = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in SshArguments.Build(lease.Host, command: command))
            startInfo.ArgumentList.Add(argument);
        foreach (var variable in lease.Environment)
            startInfo.Environment[variable.Key] = variable.Value;
        var result = await processRunner.RunAsync(startInfo, cancellationToken);
        result.EnsureSuccess("Browse remote directory");
        return ParseDirectoryListing(result.StandardOutput, remotePath);
    }

    public static List<RemoteFileEntry> ParseDirectoryListing(string output, string remotePath)
    {
        var entries = new List<RemoteFileEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line.TrimEnd('\r'),
                @"^(?<permissions>\S+)\s+\d+\s+\S+\s+\S+\s+(?<size>\d+)\s+(?<month>[A-Za-z]{3})\s+(?<day>\d{1,2})\s+(?<time>\d{2}:\d{2}|\d{4})\s+(?<name>.+)$",
                RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var name = match.Groups["name"].Value;
            var permissions = match.Groups["permissions"].Value;
            if (permissions.StartsWith('l'))
            {
                var target = name.LastIndexOf(" -> ", StringComparison.Ordinal);
                if (target >= 0) name = name[..target];
            }
            if (name is "." or ".." || name.Contains('/')) continue;
            var timestamp = match.Groups["time"].Value;
            var year = timestamp.Contains(':') ? DateTime.UtcNow.Year : int.Parse(timestamp, CultureInfo.InvariantCulture);
            var time = timestamp.Contains(':') ? timestamp : "00:00";
            DateTime? modified = null;
            if (DateTime.TryParseExact($"{year} {match.Groups["month"].Value} {match.Groups["day"].Value} {time}",
                "yyyy MMM d HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                if (timestamp.Contains(':') && parsed > DateTime.UtcNow.AddDays(1)) parsed = parsed.AddYears(-1);
                modified = parsed;
            }
            entries.Add(new RemoteFileEntry
            {
                Name = name, FullPath = remotePath.TrimEnd('/') + "/" + name,
                IsDirectory = permissions.StartsWith('d'), Permissions = permissions,
                Size = long.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture), LastModified = modified
            });
        }
        return entries.OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<FileTransferResult>> UploadToGroup(IEnumerable<ServerHost> hosts, string localPath,
        string remoteBasePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var results = new List<FileTransferResult>();
        foreach (var host in hosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var exitCode = await UploadFile(host, localPath, remoteBasePath, recursive, cancellationToken);
                results.Add(new(host, localPath, exitCode, exitCode == 0 ? null : $"SCP exited with code {exitCode}."));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("SCP upload failed for {Alias}: {Error}", host.Alias, exception.Message);
                results.Add(new(host, localPath, null, exception.Message));
            }
        }
        return results;
    }

    public async Task<List<FileTransferResult>> DownloadFromGroup(IEnumerable<ServerHost> hosts, string remotePath,
        string localBaseDir, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var destinations = BuildGroupDownloadPaths(hosts, remotePath, localBaseDir);
        var results = new List<FileTransferResult>();
        foreach (var (host, localPath) in destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var exitCode = await DownloadFile(host, remotePath, localPath, recursive, cancellationToken);
                results.Add(new(host, localPath, exitCode, exitCode == 0 ? null : $"SCP exited with code {exitCode}."));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("SCP download failed for {Alias}: {Error}", host.Alias, exception.Message);
                results.Add(new(host, localPath, null, exception.Message));
            }
        }
        return results;
    }

    public static List<(ServerHost Host, string LocalPath)> BuildGroupDownloadPaths(
        IEnumerable<ServerHost> hosts, string remotePath, string localBaseDir)
    {
        ScpArguments.ValidatePath(remotePath);
        var baseDirectory = Path.GetFullPath(localBaseDir);
        if (!Directory.Exists(baseDirectory))
            throw new DirectoryNotFoundException($"Local destination directory does not exist: {baseDirectory}");
        var filename = SafeFilename(remotePath.TrimEnd('/').Split('/').LastOrDefault() ?? "download");
        if (filename is "." or ".." or "~") filename = "download";
        var extension = Path.GetExtension(filename);
        var stem = Path.GetFileNameWithoutExtension(filename);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinations = new List<(ServerHost, string)>();
        foreach (var host in hosts)
        {
            var hostLabel = SafeFilename(string.IsNullOrWhiteSpace(host.Alias) ? host.HostName : host.Alias);
            var candidate = Path.Combine(baseDirectory, $"{stem}-{hostLabel}{extension}");
            for (var suffix = 2; reserved.Contains(candidate) || File.Exists(candidate) || Directory.Exists(candidate); suffix++)
                candidate = Path.Combine(baseDirectory, $"{stem}-{hostLabel}-{suffix}{extension}");
            reserved.Add(candidate);
            destinations.Add((host, candidate));
        }
        return destinations;
    }

    private static string SafeFilename(string value)
    {
        var safe = string.Concat(value.Select(character => character < 32 || "<>:\"/\\|?*".Contains(character) ? '_' : character))
            .Trim(' ', '.');
        // Keep group destinations valid on Windows, even when configuration was created on Linux.
        if (safe.Length == 0) return "download";
        var baseName = safe.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(baseName, StringComparer.OrdinalIgnoreCase))
            safe = "_" + safe;
        return safe;
    }
}
