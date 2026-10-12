using Sshocked.Models;

namespace Sshocked.Services;

public static class SshConfigMerge
{
    public static SshConfigSyncResult Merge(AppConfig config, IEnumerable<ServerHost> incoming, SyncConflictResolution policy, Func<ServerHost, ServerHost, SyncConflictResolution>? resolver = null)
    {
        var added = 0;
        var updated = 0;
        var skipped = 0;
        var conflicts = 0;
        foreach (var host in incoming)
        {
            var existing = FindMatch(config.Hosts, host);
            if (existing is null)
            {
                if (config.Hosts.Any(h => h.Id == host.Id))
                    host.Id = Guid.NewGuid().ToString("N");
                config.Hosts.Add(host);
                added++;
                continue;
            }

            if (SameConnection(existing, host))
                continue;
            conflicts++;
            var resolution = resolver?.Invoke(existing, host) ?? policy;
            switch (resolution)
            {
                case SyncConflictResolution.OpenSshWins:
                    CopyConnection(host, existing);
                    updated++;
                    break;
                case SyncConflictResolution.KeepBoth:
                    var prefix = host.Alias + "-openssh";
                    // Repeated automatic imports must not create another copy of the same conflict.
                    if (config.Hosts.Any(h => (h.Alias.Equals(prefix, StringComparison.OrdinalIgnoreCase) || h.Alias.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)) && SameEndpoint(h, host)))
                        break;
                    var alias = prefix;
                    var suffix = 2;
                    while (config.Hosts.Any(h => h.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)))
                        alias = prefix + "-" + suffix++;
                    host.Alias = alias;
                    host.Id = Guid.NewGuid().ToString("N");
                    config.Hosts.Add(host);
                    added++;
                    break;
                case SyncConflictResolution.SshockedWins:
                    break;
                default:
                    skipped++;
                    break;
            }
        }

        return new(added, updated, skipped, conflicts);
    }

    public static ServerHost? FindMatch(IEnumerable<ServerHost> hosts, ServerHost incoming)
    {
        var list = hosts.ToList();
        var exact = list.FirstOrDefault(h => h.Alias.Equals(incoming.Alias, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault(h => h.Id == incoming.Id);
        if (exact is not null)
            return exact;
        var fuzzy = list.Where(h => h.HostName.Equals(incoming.HostName, StringComparison.OrdinalIgnoreCase) && h.User.Equals(incoming.User, StringComparison.Ordinal)).Take(2).ToList();
        // Ambiguous endpoint matches are new hosts, rather than an arbitrary overwritten host.
        return fuzzy.Count == 1 ? fuzzy[0] : null;
    }

    public static bool SameConnection(ServerHost a, ServerHost b) => a.Alias.Equals(b.Alias, StringComparison.OrdinalIgnoreCase) && SameEndpoint(a, b);
    private static bool SameEndpoint(ServerHost a, ServerHost b) => a.HostName == b.HostName && a.User == b.User && a.Port == b.Port && a.IdentityFile == b.IdentityFile && NormalizeOptions(a.CustomSshOptions) == NormalizeOptions(b.CustomSshOptions) && a.ProxyJump == b.ProxyJump;
    private static string NormalizeOptions(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return "";
        try
        {
            return string.Join('\n', SshConfigExporter.ParseOptions(options).Select(option =>
            {
                SshConfigSyntaxDirective parsed = SshConfigSyntax.ParseDirective(option.Key + "=" + option.Value, comments: false)!;
                return parsed.Keyword + "=" + parsed.Value;
            }));
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return options.Trim();
        }
    }

    private static void CopyConnection(ServerHost source, ServerHost target)
    {
        target.Alias = source.Alias;
        target.HostName = source.HostName;
        target.User = source.User;
        target.Port = source.Port;
        target.IdentityFile = source.IdentityFile;
        target.CustomSshOptions = source.CustomSshOptions;
        target.ProxyJump = source.ProxyJump;
        // OpenSSH does not encode sshocked's password/agent/secret-provider selection.
        if (source.IdentityFile is not null || target.AuthType == AuthType.CustomConfig)
            target.AuthType = source.AuthType;
    }
}
