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
        if (host.AuthType == AuthType.CustomConfig && !string.IsNullOrWhiteSpace(host.CustomSshOptions))
            args.AddRange(Tokenize(host.CustomSshOptions));
        if (tty) args.Add("-t");
        args.Add("--");
        args.Add($"{host.User}@{host.HostName}");
        if (command != null) args.Add(command);
        return args;
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
                (options[i + 1] == quote && quote != '\0' || quote == '\0' && (char.IsWhiteSpace(options[i + 1]) || options[i + 1] is '\'' or '"')))
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
