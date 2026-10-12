using System.Text;

namespace Sshocked.Services;

/// <summary>Small OpenSSH tokenizer shared by import and public-key reference discovery.</summary>
internal static class SshConfigSyntax
{
    internal sealed record Directive(string Keyword, string Value, string? RawValue = null);

    internal static Directive? ParseDirective(string line, bool comments = true)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) return null;
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]) && trimmed[end] != '=') end++;
        if (end == trimmed.Length) return null;
        var keyword = trimmed[..end].ToLowerInvariant();
        var value = trimmed[end..].TrimStart(' ', '\t', '=');
        // Command directives are interpreted by a shell; their embedded quotes are significant.
        if (keyword is "proxycommand" or "localcommand" or "remotecommand") return new(keyword, value, value);
        // Preserve quoted '#' and whitespace; an unquoted '#' starts a comment.
        var raw = comments ? StripComment(value) : value;
        var tokens = Tokenize(raw);
        return tokens.Count == 0 ? null : new(keyword, string.Join(' ', tokens), raw.TrimEnd());
    }

    private static string StripComment(string value)
    {
        char quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length) { i++; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '"' or '\'') quote = c;
            else if (c == '#' && (i == 0 || char.IsWhiteSpace(value[i - 1]))) return value[..i];
        }
        return value;
    }

    internal static List<string> Tokenize(string value, bool comments = false)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length && (value[i + 1] is '"' or '\'' or '\\' || char.IsWhiteSpace(value[i + 1])))
            {
                token.Append(value[++i]);
                continue;
            }
            if (quote != '\0')
            {
                if (c == quote) quote = '\0'; else token.Append(c);
            }
            else if (c is '"' or '\'') quote = c;
            else if (comments && c == '#' && token.Length == 0) break;
            else if (char.IsWhiteSpace(c))
            {
                if (token.Length > 0) { tokens.Add(token.ToString()); token.Clear(); }
            }
            else token.Append(c);
        }
        if (quote != '\0') throw new FormatException("Unclosed quote in SSH configuration.");
        if (token.Length > 0) tokens.Add(token.ToString());
        return tokens;
    }

    internal static string Quote(string value)
        => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
