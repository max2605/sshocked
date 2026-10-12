using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

/// <summary>Owns only a marked section or a dedicated Include file; all replacements keep a backup.</summary>
public sealed class SshConfigExporter : ISshConfigExporter
{
    public const string BeginMarker = "# SSHOCKED_BEGIN";
    public const string EndMarker = "# SSHOCKED_END";
    public static string DefaultConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    public SshConfigExportResult Export(IEnumerable<ServerHost> hosts, SshConfigSyncSettings settings, string? configPath = null)
    {
        var path = Path.GetFullPath(configPath ?? DefaultConfigPath);
        RejectSymbolicLink(path);
        var original = File.Exists(path) ? File.ReadAllText(path) : "";
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var selected = hosts.Where(h => !settings.ExcludeHostIds.Contains(h.Id, StringComparer.Ordinal) &&
                (settings.ExportAllHosts || h.Tags.Any(tag => settings.ExportTags.Contains(tag, StringComparer.OrdinalIgnoreCase))))
            .OrderBy(h => h.Alias, StringComparer.OrdinalIgnoreCase).ToList();
        // Validate the entire export before changing either file.
        var generated = BuildHosts(selected, settings, newline);
        var written = new List<string>();
        var backups = new List<string>();
        if (settings.Strategy == ExportStrategy.IncludeFile)
        {
            var include = ResolveIncludePath(settings.IncludeFilePath);
            RejectSymbolicLink(include);
            if (PathEquals(include, path)) throw new InvalidOperationException("The include file must differ from the main SSH config.");
            var includeOriginal = File.Exists(include) ? File.ReadAllText(include) : "";
            // A dedicated file must contain only our marked block; refuse to claim somebody else's file.
            if (!string.IsNullOrWhiteSpace(includeOriginal) && !ContainsOnlyManagedBlock(includeOriginal))
                throw new InvalidOperationException("The include destination contains unmanaged content. Choose a new file.");
            var includeText = Wrap(generated, newline);
            var directive = "Include " + SshConfigSyntax.Quote(include) + newline;
            var preambleEnd = FindInsertionOffset(original);
            var hasInclude = original[..preambleEnd].Split('\n').Any(line =>
            {
                var parsed = SshConfigSyntax.ParseDirective(line);
                return parsed is { Keyword: "include" } && PathEquals(ExpandHome(parsed.Value), include);
            });
            var separator = preambleEnd > 0 && original[preambleEnd - 1] != '\n' ? newline : "";
            var mainText = hasInclude ? original : original[..preambleEnd] + separator + directive + original[preambleEnd..];
            WriteChanged(include, includeOriginal, includeText, written, backups);
            WriteChanged(path, original, mainText, written, backups);
        }
        else
        {
            var updated = ReplaceManagedBlock(original, Wrap(generated, newline));
            WriteChanged(path, original, updated, written, backups);
        }
        return new(selected.Count, written.Count > 0, written, backups);
    }

    public static string ReplaceManagedBlock(string original, string managedBlock)
    {
        var markers = FindMarkers(original);
        if (markers is null)
        {
            var offset = FindInsertionOffset(original);
            var separator = offset > 0 && original[offset - 1] != '\n' ? (original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n") : "";
            return original[..offset] + separator + managedBlock + original[offset..];
        }
        return original[..markers.Value.Start] + managedBlock + original[markers.Value.End..];
    }

    private static bool ContainsOnlyManagedBlock(string value)
    {
        var markers = FindMarkers(value);
        return markers is not null && string.IsNullOrWhiteSpace(value[..markers.Value.Start]) &&
            string.IsNullOrWhiteSpace(value[markers.Value.End..]);
    }

    private static int FindInsertionOffset(string text)
    {
        var offset = 0;
        foreach (var line in text.Split('\n'))
        {
            var directive = SshConfigSyntax.ParseDirective(line);
            if (directive?.Keyword is "host" or "match") return offset;
            offset += line.Length + 1;
        }
        return text.Length;
    }

    private static (int Start, int End)? FindMarkers(string text)
    {
        int? start = null;
        int? end = null;
        var offset = 0;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed == BeginMarker || trimmed.StartsWith(BeginMarker + ":", StringComparison.Ordinal))
            {
                if (start is not null || end is not null) throw new InvalidOperationException("Duplicate SSHOCKED markers; config was not changed.");
                start = offset;
            }
            else if (trimmed == EndMarker)
            {
                if (start is null || end is not null) throw new InvalidOperationException("Unbalanced SSHOCKED markers; config was not changed.");
                end = Math.Min(text.Length, offset + line.Length + 1);
            }
            offset += line.Length + 1;
        }
        if (start is not null && end is null) throw new InvalidOperationException("Missing SSHOCKED_END marker; config was not changed.");
        return start is null ? null : (start.Value, end!.Value);
    }

    private static string Wrap(string hosts, string newline)
        // Host * restores global/default scope for directives following the managed section or Include.
        => BeginMarker + ": auto-generated, do not edit manually" + newline + hosts + "Host *" + newline + EndMarker + newline;

    private static string BuildHosts(IEnumerable<ServerHost> hosts, SshConfigSyncSettings settings, string newline)
    {
        var result = new StringBuilder();
        foreach (var host in hosts)
        {
            ValidateValue(host.Alias);
            var aliases = host.Alias.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (aliases.Length == 0 || aliases.Any(alias => alias.StartsWith('-') || alias.Contains('#') || alias.Contains('"') || alias.Contains('\'')))
                throw new InvalidOperationException($"Invalid SSH host alias: {host.Alias}");
            ValidateValue(host.Id);
            result.Append("# sshocked-id: ").Append(host.Id).Append(newline);
            result.Append("Host ").Append(string.Join(' ', aliases)).Append(newline);
            AppendDirective(result, "HostName", host.HostName, newline);
            if (settings.ExportUser && !string.IsNullOrWhiteSpace(host.User)) AppendDirective(result, "User", host.User, newline);
            if (settings.ExportPort)
            {
                if (host.Port is < 1 or > 65535) throw new InvalidOperationException($"Invalid SSH port for {host.Alias}.");
                AppendDirective(result, "Port", host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), newline);
            }
            if (settings.ExportIdentityFile && host.AuthType == AuthType.SshKey && !string.IsNullOrWhiteSpace(host.IdentityFile))
                AppendDirective(result, "IdentityFile", host.IdentityFile, newline);
            if (!string.IsNullOrWhiteSpace(host.ProxyJump)) AppendDirective(result, "ProxyJump", host.ProxyJump, newline);
            if (settings.ExportCustomOptions && !string.IsNullOrWhiteSpace(host.CustomSshOptions))
                foreach (var option in ParseOptions(host.CustomSshOptions)) AppendDirective(result, option.Key, option.Value, newline, quote: false);
            result.Append(newline);
        }
        return result.ToString();
    }

    internal static IEnumerable<KeyValuePair<string, string>> ParseOptions(string options)
    {
        var args = SshConfigSyntax.Tokenize(options);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string value;
            string keyword;
            if (arg == "-o" || arg.StartsWith("-o", StringComparison.Ordinal))
            {
                value = arg == "-o" ? (++i < args.Count ? args[i] : throw new InvalidOperationException("Missing -o value.")) : arg[2..];
                var directive = SshConfigSyntax.ParseDirective(value, comments: false) ?? throw new InvalidOperationException("Custom SSH options need -o Keyword=value.");
                keyword = directive.Keyword;
                value = directive.RawValue ?? directive.Value;
            }
            else if (arg is "-J" or "-i" or "-p" or "-l")
            {
                keyword = arg switch { "-J" => "ProxyJump", "-i" => "IdentityFile", "-p" => "Port", _ => "User" };
                value = ++i < args.Count ? args[i] : throw new InvalidOperationException($"Missing {arg} value.");
            }
            else throw new InvalidOperationException($"Cannot export custom CLI flag '{arg}' to an SSH config directive. Use -o Keyword=value.");
            if (!keyword.All(char.IsAsciiLetter) || keyword.Equals("host", StringComparison.OrdinalIgnoreCase) ||
                keyword.Equals("match", StringComparison.OrdinalIgnoreCase) || keyword.Equals("include", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Host, Match and Include cannot be injected through custom SSH options.");
            yield return new(keyword, value);
        }
    }

    private static void AppendDirective(StringBuilder target, string key, string value, string newline, bool quote = true)
    {
        ValidateValue(value);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"SSH directive {key} must not be empty.");
        var command = key.Equals("ProxyCommand", StringComparison.OrdinalIgnoreCase) || key.Equals("LocalCommand", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("RemoteCommand", StringComparison.OrdinalIgnoreCase);
        target.Append("    ").Append(key).Append(' ').Append(command || !quote ? value : SshConfigSyntax.Quote(value)).Append(newline);
    }

    private static void ValidateValue(string value)
    {
        if (value.Any(c => c is '\r' or '\n' or '\0')) throw new InvalidOperationException("SSH config values must not contain line breaks or NUL.");
    }

    public static string ResolveIncludePath(string? configured)
    {
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "sshocked", "ssh-config-include")
            : ExpandHome(configured);
        return Path.GetFullPath(path);
    }

    private static string ExpandHome(string path)
        => path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]) : path;

    private static bool PathEquals(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void RejectSymbolicLink(string path)
    {
        // Atomic replacement would replace the link itself, disconnecting a user's managed dotfile.
        // LinkTarget also identifies broken symbolic links that File.Exists does not see.
        if (new FileInfo(path).LinkTarget is not null)
            throw new InvalidOperationException($"Cannot export to symbolic link '{path}'. Choose a physical SSH config or include destination; the link and its target were left unchanged.");
    }

    private static void WriteChanged(string path, string original, string content, List<string> written, List<string> backups)
    {
        if (original == content) return;
        RejectSymbolicLink(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".sshocked-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, File.Exists(path) ? File.GetUnixFileMode(path) : UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (File.Exists(path))
            {
                // Abort if an editor changed the file after the preview was constructed.
                if (File.ReadAllText(path) != original) throw new IOException("SSH config changed during export. Retry after reviewing the file.");
                var backup = path + ".sshocked-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6] + ".bak";
                File.Copy(path, backup, overwrite: false);
                backups.Add(backup);
            }
            else if (original.Length != 0) throw new IOException("SSH config was removed during export. Retry after reviewing the file.");
            RejectSymbolicLink(path);
            File.Move(temporary, path, overwrite: true);
            written.Add(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
