using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Services;

public static class MonitoringScope
{
    public static string Fingerprint(MonitorDefinition definition, ServerHost? host)
    {
        var endpoint = host is null ? null : new ServerHost
        {
            Id = host.Id,
            HostName = host.HostName,
            User = host.User,
            Port = host.Port,
            AuthType = host.AuthType,
            IdentityFile = host.IdentityFile,
            CustomSshOptions = host.CustomSshOptions,
            ProxyJump = host.ProxyJump,
            AzureBastion = host.AzureBastion,
            KeySecretRef = host.KeySecretRef,
            PasswordSecretRef = host.PasswordSecretRef,
            SudoPasswordSecretRef = host.SudoPasswordSecretRef
        };
        var text = JsonSerializer.Serialize(definition, MonitoringJsonContext.Default.MonitorDefinition) + "\n" +
            JsonSerializer.Serialize(endpoint, AppConfigContext.Default.ServerHost);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public static bool Matches(MonitoringRecord record, MonitorDefinition definition, ServerHost? host) =>
        record.State.TryGetValue(MonitoringStateKeys.Scope, out var saved) && saved == Fingerprint(definition, host);
}
