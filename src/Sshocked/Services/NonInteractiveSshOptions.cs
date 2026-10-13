using System.Diagnostics;
using Sshocked.Models;

namespace Sshocked.Services;

public static class NonInteractiveSshOptions
{
    public static void Apply(ProcessStartInfo info, ServerHost host, bool clearForwardings = true)
    {
        Validate(host.CustomSshOptions);
        // OpenSSH uses the first value; apply these before config/custom options.
        foreach (var option in new[] { "StrictHostKeyChecking=yes", "NumberOfPasswordPrompts=1",
            "ConnectTimeout=10", "RequestTTY=no", "PermitLocalCommand=no", "ControlMaster=no", "ControlPersist=no", "ControlPath=none",
            clearForwardings ? "ClearAllForwardings=yes" : "ClearAllForwardings=no", "ForkAfterAuthentication=no",
            string.IsNullOrEmpty(host.SshPassword) ? "BatchMode=yes" : "BatchMode=no" })
        {
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add(option);
        }
    }

    public static void Validate(string? customOptions)
    {
        if (string.IsNullOrWhiteSpace(customOptions)) return;
        var tokens = SshArguments.Tokenize(customOptions);
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token is "-v" or "-vv" or "-vvv" or "-q" or "-C" or "-4" or "-6" or "-T") continue;
            if (token.StartsWith("-o", StringComparison.Ordinal))
            {
                var setting = token.Length > 2 ? token[2..] : ++index < tokens.Count ? tokens[index] : throw new ArgumentException("SSH -o requires a setting.");
                var name = setting.Split(['=', ' ', '\t'], 2)[0];
                if (new[] { "RequestTTY", "LocalForward", "RemoteForward", "DynamicForward", "LocalCommand", "PermitLocalCommand",
                    "ControlMaster", "ControlPath", "ControlPersist", "ForkAfterAuthentication", "SessionType", "RemoteCommand", "ClearAllForwardings" }
                    .Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("This SSH setting can start interactive, background or forwarding behavior and is not permitted in noninteractive probes/batches.");
                continue;
            }
            if (token.Length >= 2 && token[0] == '-' && token[1] is 'i' or 'J' or 'F')
            {
                if (token.Length == 2 && ++index >= tokens.Count) throw new ArgumentException("SSH option requires a literal value.");
                continue;
            }
            throw new ArgumentException("Only safe SSH -o settings, identity/config/jump options and verbosity/network flags are supported by noninteractive probes/batches.");
        }
    }
}
