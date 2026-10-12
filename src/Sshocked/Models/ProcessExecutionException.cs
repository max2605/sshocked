namespace Sshocked.Models;

public sealed class ProcessExecutionException(string operation, int exitCode, string standardError) : Exception($"{operation} failed (exit code {exitCode}). {standardError.Trim()}")
{
    public int ExitCode { get; } = exitCode;
    public string StandardError { get; } = standardError;
}
