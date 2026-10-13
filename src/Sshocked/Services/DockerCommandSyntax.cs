namespace Sshocked.Services;

internal static class DockerCommandSyntax
{
    public static string Identifier(Models.ContainerModel container) => string.IsNullOrWhiteSpace(container.ContainerId) ? container.Name : container.ContainerId;

    public static string QuoteCommand(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Contains('\0')) throw new ArgumentException("Commands cannot contain NUL.", nameof(command));
        return "'" + command.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    public static string QuoteContainerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => c is '\0' or '\r' or '\n'))
            throw new ArgumentException("Docker container name must be nonempty and contain no line breaks or NUL.", nameof(name));
        return "'" + name.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
