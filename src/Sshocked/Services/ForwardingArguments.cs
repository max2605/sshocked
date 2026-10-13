using System.Diagnostics;
using System.Text;
using Sshocked.Models;

namespace Sshocked.Services;

public static class ForwardingArguments
{
    public static ProcessStartInfo Build(ServerHost host, ForwardingProfile profile, IReadOnlyDictionary<string, string?> environment)
    {
        ForwardingValidation.Validate(profile);
        ValidateCustomOptions(host.CustomSshOptions);
        var info = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        NonInteractiveSshOptions.Apply(info, host, clearForwardings: false);
        info.ArgumentList.Add("-F");
        info.ArgumentList.Add("none");
        foreach (var option in new[] { "ExitOnForwardFailure=yes", "LogLevel=DEBUG1", "ForkAfterAuthentication=no",
            "ServerAliveInterval=15", "ServerAliveCountMax=3", "ForwardAgent=no", "ForwardX11=no" })
        { info.ArgumentList.Add("-o"); info.ArgumentList.Add(option); }
        info.ArgumentList.Add("-N");
        info.ArgumentList.Add("-T");
        info.ArgumentList.Add("-n");
        var address = ForwardingValidation.Address(ForwardingValidation.BindAddress(profile.BindAddress).ToString()) + ":" + profile.ListenPort;
        if (profile.Kind != ForwardingKind.Dynamic) address += ":" + ForwardingValidation.Address(profile.TargetHost) + ":" + profile.TargetPort;
        info.ArgumentList.Add(profile.Kind switch { ForwardingKind.Local => "-L", ForwardingKind.Remote => "-R", _ => "-D" });
        info.ArgumentList.Add(address);
        foreach (var argument in SshArguments.Build(host)) info.ArgumentList.Add(argument);
        foreach (var variable in environment) info.Environment[variable.Key] = variable.Value;
        return info;
    }

    private static void ValidateCustomOptions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var tokens = SshArguments.Tokenize(text);
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token is "-q" or "-f" or "-g" or "-N" or "-n" or "-T" or "-t" || token.StartsWith("-L", StringComparison.Ordinal)
                || token.StartsWith("-R", StringComparison.Ordinal) || token.StartsWith("-D", StringComparison.Ordinal)
                || token.StartsWith("-E", StringComparison.Ordinal) || token.StartsWith("-F", StringComparison.Ordinal)
                || token.StartsWith("-M", StringComparison.Ordinal) || token.StartsWith("-S", StringComparison.Ordinal)
                || token.StartsWith("-O", StringComparison.Ordinal) || token.StartsWith("-W", StringComparison.Ordinal))
                throw new ArgumentException("Custom SSH options cannot add forwards, daemonize, redirect diagnostics or override managed session behavior.");
            if (!token.StartsWith("-o", StringComparison.Ordinal)) continue;
            var setting = token.Length > 2 ? token[2..] : ++index < tokens.Count ? tokens[index] : throw new ArgumentException("SSH -o needs a value.");
            var name = setting.Split(['=', ' ', '\t'], 2)[0];
            if (new[] { "LocalForward", "RemoteForward", "DynamicForward", "ClearAllForwardings", "StdioForwardHost", "SessionType" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Configure forwarding routes only through the forwarding profile.");
        }
    }
}
