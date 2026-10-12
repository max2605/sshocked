using System.Globalization;
using System.Text;
using Sshocked.Models;

namespace Sshocked.Services;

public static class SshArguments
{
    public static IReadOnlyList<string> Build(ServerHost host, bool tty = false, string? command = null)
    {
        if (host.Port is < 1 or > 65535) throw new ArgumentException("SSH port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(host.HostName) || host.HostName.StartsWith('-')) throw new ArgumentException("An SSH hostname is required and cannot start with '-'.");
        if (string.IsNullOrWhiteSpace(host.User) || host.User.StartsWith('-')) throw new ArgumentException("An SSH username is required and cannot start with '-'.");
        var args = new List<string> { "-p", host.Port.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(host.ProxyJump)) args.AddRange(["-J", host.ProxyJump]);
        if (!string.IsNullOrWhiteSpace(host.SshHostKeyAlias)) args.AddRange(["-o", "HostKeyAlias=" + host.SshHostKeyAlias]);
        if (host.AuthType == AuthType.SshKey && !string.IsNullOrWhiteSpace(host.IdentityFile))
            args.AddRange(["-i", host.IdentityFile]);
        if (!string.IsNullOrWhiteSpace(host.CustomSshOptions))
        {
            var options = Tokenize(host.CustomSshOptions);
            if (!string.IsNullOrWhiteSpace(host.SshHostKeyAlias)) ValidateTunnelOptions(options);
            args.AddRange(options);
        }
        if (tty) args.Add("-t");
        args.Add("--");
        args.Add($"{host.User}@{host.HostName}");
        if (command != null) args.Add(command);
        return args;
    }

    public static void ValidateTunnelOptions(IReadOnlyList<string> options)
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Hostname", "Port", "ProxyCommand", "ProxyJump", "HostKeyAlias", "CanonicalizeHostname", "CanonicalDomains", "LocalCommand", "PermitLocalCommand" };
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (option.StartsWith("-o", StringComparison.Ordinal))
            {
                var setting = option.Length > 2 ? option[2..] : ++index < options.Count ? options[index] : throw new ArgumentException("SSH -o option needs a value.");
                var name = setting.Split(['=', ' ', '\t'], 2)[0];
                if (forbidden.Contains(name)) throw new ArgumentException($"SSH setting '{name}' cannot override an Azure Bastion tunnel connection.");
            }
            else if (option is not ("-v" or "-vv" or "-vvv" or "-q" or "-C" or "-4" or "-6"))
                throw new ArgumentException($"SSH option '{option}' is unsupported for a managed Bastion tunnel. Use -o for SSH settings.");
        }
    }

    /// <summary>Splits user-supplied option text; never evaluates a shell or expands variables.</summary>
    public static IReadOnlyList<string> Tokenize(string options)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        var quote = '\0';
        var started = false;
        for (var i = 0; i < options.Length; i++)
        {
            var c = options[i];
            if (c == '\\' && i + 1 < options.Length &&
                (quote == '"' && options[i + 1] is '"' or '\\' || quote == '\0' && (char.IsWhiteSpace(options[i + 1]) || options[i + 1] is '\'' or '"' or '\\')))
            { token.Append(options[++i]); started = true; continue; }
            if (quote != '\0')
            { if (c == quote) quote = '\0'; else token.Append(c); started = true; continue; }
            if (c is '\'' or '"') { quote = c; started = true; continue; }
            if (char.IsWhiteSpace(c))
            { if (started) { result.Add(token.ToString()); token.Clear(); started = false; } continue; }
            token.Append(c); started = true;
        }
        if (quote != '\0') throw new ArgumentException("Unterminated quote in custom SSH options.");
        if (started) result.Add(token.ToString());
        return result;
    }
}
