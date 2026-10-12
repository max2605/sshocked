using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SshKeyDiscoveryService(ILogger<SshKeyDiscoveryService> logger) : ISshKeyDiscoveryService
{
    public IReadOnlyList<SshKeyInfo> ListAvailableKeys()
        => DiscoverKeys(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"));

    public IReadOnlyList<SshKeyInfo> DiscoverKeys(string sshDirectory)
    {
        if (!Directory.Exists(sshDirectory)) return [];
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            foreach (var path in Directory.EnumerateFiles(sshDirectory, "id_*"))
                if (!path.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) && File.Exists(path + ".pub"))
                    paths.Add(Path.GetFullPath(path));

            var configPath = Path.Combine(sshDirectory, "config");
            if (File.Exists(configPath))
            {
                foreach (var line in File.ReadLines(configPath))
                {
                    try
                    {
                        var parts = SshConfigSyntax.ParseDirective(line);
                        if (parts is not { Keyword: "identityfile" }) continue;
                        var path = ExpandPath(parts.Value, sshDirectory);
                        if (path is not null && File.Exists(path) && File.Exists(path + ".pub")) paths.Add(path);
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
                    {
                        logger.LogDebug(ex, "Skipping an invalid SSH key reference in {Path}", configPath);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to discover all SSH keys in {Directory}", sshDirectory);
        }

        var agent = GetAgentStatus();
        var loaded = agent.Keys.Select(key => key.Fingerprint).ToHashSet(StringComparer.Ordinal);
        var keys = new List<SshKeyInfo>();
        foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var key = ParsePublicKey(File.ReadAllText(path + ".pub"), path);
                if (key is not null) keys.Add(key with { IsAgentLoaded = loaded.Contains(key.Fingerprint) });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Unable to read public key for {Path}", path);
            }
        }
        return keys;
    }

    public SshAgentStatus GetAgentStatus()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("ssh-add")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-L");
            if (!process.Start()) return new(false, [], "ssh-add could not be started.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return new(false, [], "ssh-agent did not respond.");
            }
            // Exit 1 means an accessible agent with no identities; exit 2 means no agent.
            if (process.ExitCode == 1) return new(true, [], "ssh-agent is running, with no keys loaded.");
            if (process.ExitCode != 0) return new(false, [], "ssh-agent is unavailable.");
            var keys = output.GetAwaiter().GetResult().Split('\n')
                .Select(line => ParsePublicKey(line))
                .OfType<SshKeyInfo>()
                .Select(key => key with { IsAgentLoaded = true })
                .ToList();
            _ = error.GetAwaiter().GetResult();
            return new(true, keys, $"ssh-agent is running, with {keys.Count} key(s) loaded.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogDebug(ex, "SSH agent detection is unavailable");
            return new(false, [], "ssh-add is unavailable or ssh-agent is not running.");
        }
    }

    public SshKeyInfo? ParsePublicKey(string publicKey, string privateKeyPath = "")
    {
        var parts = publicKey.Trim().Split([' ', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !(parts[0].StartsWith("ssh-", StringComparison.Ordinal) ||
            parts[0].StartsWith("ecdsa-", StringComparison.Ordinal) || parts[0].StartsWith("sk-", StringComparison.Ordinal))) return null;
        try
        {
            var bytes = Convert.FromBase64String(parts[1]);
            // OpenSSH public blobs begin with a big-endian length-prefixed algorithm name.
            if (bytes.Length < 4) return null;
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes);
            if (length == 0 || length > bytes.Length - 4 ||
                System.Text.Encoding.ASCII.GetString(bytes, 4, (int)length) != parts[0]) return null;
            var fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(bytes)).TrimEnd('=');
            var algorithm = parts[0].Contains("ed25519", StringComparison.Ordinal) ? "ED25519" :
                parts[0].Contains("ecdsa", StringComparison.Ordinal) ? "ECDSA" :
                parts[0].Contains("rsa", StringComparison.Ordinal) ? "RSA" : parts[0];
            return new(privateKeyPath, algorithm, fingerprint, parts.Length == 3 ? parts[2] : "", parts[0] + " " + parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? ExpandPath(string value, string sshDirectory)
    {
        if (value.Contains('%') || value.Contains('$') || value.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        var home = Path.GetDirectoryName(Path.GetFullPath(sshDirectory))!;
        if (value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
            return Path.GetFullPath(Path.Combine(home, value[2..]));
        if (value.StartsWith('~')) return null;
        // OpenSSH resolves relative IdentityFile paths against the user's home directory.
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(home, value));
    }
}
