using System.Globalization;
using Sshocked.Models;

namespace Sshocked.Services;

public static class ScpArguments
{
    private static readonly HashSet<string> ValueOptions = ["-o", "-i", "-F", "-J", "-c", "-S"];
    private static readonly HashSet<string> FlagOptions = ["-C", "-v", "-vv", "-vvv", "-q", "-4", "-6"];

    public static IReadOnlyList<string> Build(ServerHost host, string localPath, string remotePath,
        bool upload, bool recursive = false)
    {
        // Validate the shared connection fields before constructing an SCP endpoint.
        _ = SshArguments.Build(host);
        if (host.User.Any(char.IsWhiteSpace) || host.User.IndexOfAny(['@', ':', '/', '\\']) >= 0 ||
            host.HostName.Any(char.IsWhiteSpace) || host.HostName.IndexOfAny(['@', '/', '\\']) >= 0)
            throw new ArgumentException("The SCP hostname and username must be single endpoint values.");
        ValidatePath(localPath);
        ValidatePath(remotePath);
        var arguments = new List<string> { "-P", host.Port.ToString(CultureInfo.InvariantCulture) };
        if (recursive) arguments.Add("-r");
        if (host.AuthType == AuthType.SshKey && !string.IsNullOrWhiteSpace(host.IdentityFile))
            arguments.AddRange(["-i", host.IdentityFile]);
        if (!string.IsNullOrWhiteSpace(host.CustomSshOptions))
            AddCustomOptions(arguments, SshArguments.Tokenize(host.CustomSshOptions));
        if (!string.IsNullOrWhiteSpace(host.ProxyJump))
            arguments.AddRange(["-J", host.ProxyJump]);
        if (!string.IsNullOrWhiteSpace(host.SshHostKeyAlias))
            arguments.AddRange(["-o", "HostKeyAlias=" + host.SshHostKeyAlias]);

        var hostname = host.HostName.Contains(':') && !host.HostName.StartsWith('[')
            ? $"[{host.HostName}]" : host.HostName;
        // Modern OpenSSH SCP uses SFTP: remote paths are literal protocol paths, not shell code.
        // Legacy -O is deliberately not accepted because it changes path quoting semantics.
        var remote = $"{host.User}@{hostname}:{remotePath}";
        var local = Path.GetFullPath(Environment.ExpandEnvironmentVariables(localPath));
        arguments.Add("--");
        arguments.Add(upload ? local : remote);
        arguments.Add(upload ? remote : local);
        return arguments;
    }

    private static void AddCustomOptions(List<string> destination, IReadOnlyList<string> options)
    {
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (FlagOptions.Contains(option)) { destination.Add(option); continue; }
            var name = option.Length > 2 ? option[..2] : option;
            if (!ValueOptions.Contains(name))
                throw new ArgumentException($"SSH option '{option}' is not supported for SCP. Use -o for connection options.");
            destination.Add(option);
            if (option.Length > 2) continue;
            if (++index >= options.Count)
                throw new ArgumentException($"SCP option '{option}' needs a value.");
            destination.Add(options[index]);
        }
    }

    public static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("A file path is required and must not contain NUL or line breaks.", nameof(path));
    }
}
