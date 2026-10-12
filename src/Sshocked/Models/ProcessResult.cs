namespace Sshocked.Models;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public void EnsureSuccess(string operation)
    {
        if (ExitCode != 0)
            throw new ProcessExecutionException(operation, ExitCode, StandardError);
    }
}

public sealed class ProcessExecutionException(string operation, int exitCode, string standardError)
    : Exception($"{operation} failed (exit code {exitCode}). {standardError.Trim()}")
{
    public int ExitCode { get; } = exitCode;
    public string StandardError { get; } = standardError;
}
