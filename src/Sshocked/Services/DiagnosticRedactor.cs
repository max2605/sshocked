using System.Text.RegularExpressions;

namespace Sshocked.Services;

internal static class DiagnosticRedactor
{
    private static readonly Regex Paths = new(@"""[A-Za-z]:\\[^""\r\n]*""|'[A-Za-z]:\\[^'\r\n]*'|(?:[A-Za-z]:\\|\\\\)[^\s'""<>]+|(?<![\w/])(?:~/|/)[A-Za-z0-9._~][^\s'""<>]+",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string? Sanitize(string? text, IReadOnlyList<string> credentials, bool includePaths)
    {
        if (text is null) return null;
        if (text.Contains("PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase)) return "[private key omitted]";
        foreach (var credential in credentials) text = text.Replace(credential, "[REDACTED]", StringComparison.Ordinal);
        text = AuditRedactor.Redact(text) ?? string.Empty;
        if (!includePaths)
        {
            try { text = Paths.Replace(text, "[path omitted]"); }
            catch (RegexMatchTimeoutException) { return "[details omitted]"; }
        }
        return text;
    }
}
