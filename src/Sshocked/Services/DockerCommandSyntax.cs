namespace Sshocked.Services;

internal static class DockerCommandSyntax
{
    public static string QuoteContainerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => c is '\0' or '\r' or '\n'))
            throw new ArgumentException("Docker container name must be nonempty and contain no line breaks or NUL.", nameof(name));
        return "'" + name.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
