namespace Sshocked.Services;

public static class AuditRedactor
{
    private static readonly System.Text.RegularExpressions.Regex SudoInput = new("(?i)(echo\\s+)(?:\"[^\"]*\"|'[^']*'|[^\\s|]+)(\\s*\\|\\s*sudo\\s+-S)");
    private static readonly System.Text.RegularExpressions.Regex MysqlPassword = new("(?i)(\\bmysql\\b[^\\r\\n]*?\\s-p)(?:\"[^\"]*\"|'[^']*'|[^\\s;]+)");
    private static readonly System.Text.RegularExpressions.Regex BearerToken = new("(?i)(Bearer\\s+)[A-Za-z0-9._~+/-]+={0,2}");
    private static readonly System.Text.RegularExpressions.Regex SensitiveFlags = new("(?i)((?:--?(?:password|passwd|passphrase|token|secret|api-key)|(?:password|passwd|token|secret|api_key))\\s*(?:=|:)??\\s*)(?:\"[^\"]*\"|'[^']*'|[^\\s;]+)");
    public static string? Redact(string? value)
    {
        if (value is null)
            return null;
        value = SudoInput.Replace(value, "$1[REDACTED]$2");
        value = MysqlPassword.Replace(value, "$1[REDACTED]");
        value = BearerToken.Replace(value, "$1[REDACTED]");
        return SensitiveFlags.Replace(value, match => match.Groups[1].Value + "[REDACTED]");
    }
}
