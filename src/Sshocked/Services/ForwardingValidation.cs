using System.Net;
using Sshocked.Models;

namespace Sshocked.Services;

public static class ForwardingValidation
{
    public static void Validate(ForwardingProfile profile)
    {
        ValidateId(profile.Id);
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80 || profile.Name.Any(char.IsControl))
            throw new ArgumentException("A forwarding profile name needs 1 to 80 characters.");
        if (string.IsNullOrWhiteSpace(profile.HostId)) throw new ArgumentException("Select a server for the forwarding profile.");
        if (!Enum.IsDefined(profile.Kind)) throw new ArgumentException("Use a local, remote or dynamic forwarding kind.");
        if (profile.ListenPort is < 1 or > 65535 || profile.StartupTimeoutSeconds is < 1 or > 120)
            throw new ArgumentException("Listener port must be 1–65535 and startup timeout 1–120 seconds.");
        var bind = BindAddress(profile.BindAddress);
        if (!IPAddress.IsLoopback(bind) && !profile.AllowNonLoopbackBind)
            throw new ArgumentException("Non-loopback listeners require an explicit AllowNonLoopbackBind profile choice.");
        if (profile.Kind != ForwardingKind.Dynamic)
        {
            if (profile.TargetPort is < 1 or > 65535) throw new ArgumentException("Target port must be 1–65535.");
            if (string.IsNullOrWhiteSpace(profile.TargetHost) || profile.TargetHost.Length > 253 ||
                profile.TargetHost.Any(char.IsControl) || profile.TargetHost.StartsWith('-') ||
                Uri.CheckHostName(profile.TargetHost) == UriHostNameType.Unknown)
                throw new ArgumentException("Target host must be a DNS name or numeric IP address.");
        }
    }

    public static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Forwarding profile IDs may contain only ASCII letters, digits, '-' and '_'.");
    }

    public static IPAddress BindAddress(string value) => value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        ? IPAddress.Loopback : IPAddress.TryParse(value, out var address) ? address
            : throw new ArgumentException("Bind address must be localhost or a numeric IPv4/IPv6 address.");
    public static string Address(string value) => value.Contains(':') ? "[" + value + "]" : value;
    public static string Route(ForwardingProfile profile) => profile.Kind switch
    {
        ForwardingKind.Local => $"local {Address(profile.BindAddress)}:{profile.ListenPort} -> remote {Address(profile.TargetHost)}:{profile.TargetPort}",
        ForwardingKind.Remote => $"remote {Address(profile.BindAddress)}:{profile.ListenPort} -> local {Address(profile.TargetHost)}:{profile.TargetPort}",
        ForwardingKind.Dynamic => $"local SOCKS {Address(profile.BindAddress)}:{profile.ListenPort}",
        _ => throw new ArgumentException("Unknown forwarding kind.")
    };
}
