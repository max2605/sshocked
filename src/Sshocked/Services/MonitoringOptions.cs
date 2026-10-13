using System.Globalization;
using Sshocked.Models;

namespace Sshocked.Services;

public static class MonitoringOptions
{
    public static string Get(MonitorDefinition definition, string key, string fallback = "") =>
        definition.Options.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? fallback;

    public static int Integer(MonitorDefinition definition, string key, int fallback, int minimum, int maximum)
    {
        var text = Get(definition, key, fallback.ToString(CultureInfo.InvariantCulture));
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
            throw new ArgumentException($"{key} must be an integer from {minimum} to {maximum}.");
        return value;
    }

    public static bool Boolean(MonitorDefinition definition, string key, bool fallback = false)
    {
        if (!bool.TryParse(Get(definition, key, fallback.ToString()), out var value))
            throw new ArgumentException($"{key} must be true or false.");
        return value;
    }

    public static string PosixScript(string script) => "sh -c " + RemoteShell.Quote("set -eu\n" + script);
    public static string PowerShellScript(string script) => "powershell.exe -NoProfile -NonInteractive -EncodedCommand " +
        Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(
            "$ErrorActionPreference='Stop';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);" + script));
}
