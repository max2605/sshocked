namespace Sshocked.Services;

/// <summary>Quotes data for a POSIX shell. Commands remain explicit, user-configured executable content.</summary>
public static class RemoteShell
{
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0'))
            throw new ArgumentException("Shell values cannot contain NUL characters.", nameof(value));
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    public static string QuotePath(string value)
    {
        if (value == "~") return "\"$HOME\"";
        if (value.StartsWith("~/", StringComparison.Ordinal))
            return "\"$HOME\"/" + Quote(value[2..]);
        return Quote(value);
    }
}
