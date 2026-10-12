using System.IO.Enumeration;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SshConfigImporter(ILogger<SshConfigImporter> logger) : ISshConfigImporter
{

    public List<ServerHost> Import(string? configPath = null)
    {
        var path = configPath ?? SshConfigExporter.DefaultConfigPath;
        if (!File.Exists(path))
        {
            logger.LogInformation("No SSH config found at {Path}", path);
            return [];
        }
        var content = ReadWithIncludes(Path.GetFullPath(path), Path.GetDirectoryName(Path.GetFullPath(path))!, [], 0);
        var hosts = Parse(content);
        logger.LogInformation("Imported {Count} host(s) from SSH config", hosts.Count);
        return hosts;
    }

    public List<ServerHost> Parse(string configContent)
    {
        var stanzas = new List<SshConfigImporterStanza>();
        var current = new SshConfigImporterStanza(["*"], []);
        stanzas.Add(current);
        var inMatch = false;
        string? nextId = null;
        foreach (var line in configContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# sshocked-id:", StringComparison.Ordinal))
            {
                nextId = trimmed["# sshocked-id:".Length..].Trim();
                continue;
            }
            var directive = SshConfigSyntax.ParseDirective(line);
            if (directive is null) continue;
            if (directive.Keyword == "host")
            {
                current = new(directive.Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries), [], nextId);
                nextId = null;
                stanzas.Add(current);
                inMatch = false;
            }
            else if (directive.Keyword == "match") inMatch = true;
            else if (!inMatch) current.Directives.Add(directive);
        }

        var aliases = stanzas.SelectMany(stanza => stanza.Patterns).Where(IsConcreteAlias)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var hosts = new List<ServerHost>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            // OpenSSH uses the first obtained value. Later Host * defaults still apply to missing values.
            var directives = new Dictionary<string, SshConfigSyntaxDirective>(StringComparer.OrdinalIgnoreCase);
            string? id = null;
            foreach (SshConfigImporterStanza stanza in stanzas.Where(stanza => Matches(stanza.Patterns, alias)))
            {
                if (stanza.Patterns.Any(pattern => pattern.Equals(alias, StringComparison.OrdinalIgnoreCase)) && stanza.Id is not null)
                    id ??= stanza.Id;
                foreach (SshConfigSyntaxDirective directive in stanza.Directives)
                    if (directive.Keyword is not ("include" or "host" or "match")) directives.TryAdd(directive.Keyword, directive);
            }
            var values = directives.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.OrdinalIgnoreCase);
            var hostName = values.GetValueOrDefault("hostname", alias).Replace("%h", alias, StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(hostName)) continue;
            var port = 22;
            if (values.TryGetValue("port", out var portValue) && (!int.TryParse(portValue, out port) || port is < 1 or > 65535))
                throw new FormatException($"Invalid SSH port for '{alias}'. Import was not applied.");
            var customOptions = directives.Where(pair => pair.Key is not ("hostname" or "user" or "port" or "identityfile" or "proxyjump"))
                .Select(pair => "-o " + SshConfigSyntax.Quote(pair.Key + "=" + (pair.Value.RawValue ?? pair.Value.Value))).ToList();
            var identity = values.GetValueOrDefault("identityfile");
            if (identity?.Equals("none", StringComparison.OrdinalIgnoreCase) == true)
            {
                customOptions.Add("-o IdentityFile=none");
                identity = null;
            }
            if (id is null || id.Length == 0 || !usedIds.Add(id))
            {
                id = Guid.NewGuid().ToString("N");
                usedIds.Add(id);
            }
            hosts.Add(new ServerHost
            {
                Id = id,
                Alias = alias,
                HostName = hostName,
                User = values.GetValueOrDefault("user", Environment.UserName),
                Port = port,
                IdentityFile = identity,
                ProxyJump = values.GetValueOrDefault("proxyjump"),
                AuthType = !string.IsNullOrWhiteSpace(identity) ? AuthType.SshKey : customOptions.Count > 0 ? AuthType.CustomConfig : AuthType.SshKey,
                CustomSshOptions = customOptions.Count > 0 ? string.Join(' ', customOptions) : null
            });
        }
        return hosts;
    }

    private static bool IsConcreteAlias(string alias)
        => alias.Length > 0 && !alias.StartsWith('!') && !alias.Contains('*') && !alias.Contains('?');

    private static bool Matches(string[] patterns, string alias)
    {
        var positive = false;
        foreach (var pattern in patterns)
        {
            var negated = pattern.StartsWith('!');
            var matches = FileSystemName.MatchesSimpleExpression(negated ? pattern[1..] : pattern, alias, ignoreCase: true);
            if (matches && negated) return false;
            if (matches) positive = true;
        }
        return positive;
    }

    private string ReadWithIncludes(string path, string sshDirectory, HashSet<string> active, int depth)
    {
        if (depth > 16 || !active.Add(path)) throw new FormatException("SSH config Include cycle or nesting limit reached.");
        try
        {
            var lines = new List<string>();
            foreach (var line in File.ReadLines(path))
            {
                var directive = SshConfigSyntax.ParseDirective(line);
                if (directive is not { Keyword: "include" }) { lines.Add(line); continue; }
                // Tokenize the original tail so quoted paths containing spaces stay one filename.
                var tail = line.Trim()["include".Length..].TrimStart(' ', '\t', '=');
                foreach (var pattern in SshConfigSyntax.Tokenize(tail, comments: true))
                {
                    var expanded = pattern.StartsWith("~/", StringComparison.Ordinal) || pattern.StartsWith("~\\", StringComparison.Ordinal)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), pattern[2..]) : pattern;
                    var full = Path.IsPathRooted(expanded) ? expanded : Path.Combine(sshDirectory, expanded);
                    var directory = Path.GetDirectoryName(full)!;
                    var filePattern = Path.GetFileName(full);
                    if (!Directory.Exists(directory)) continue;
                    foreach (var include in Directory.EnumerateFiles(directory, filePattern).Order(StringComparer.Ordinal))
                        lines.Add(ReadWithIncludes(Path.GetFullPath(include), sshDirectory, active, depth + 1));
                }
            }
            return string.Join('\n', lines);
        }
        finally { active.Remove(path); }
    }
}
