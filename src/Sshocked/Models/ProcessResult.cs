namespace Sshocked.Models;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public void EnsureSuccess(string operation)
    {
        if (ExitCode != 0)
            throw new ProcessExecutionException(operation, ExitCode, StandardError);
    }
}
