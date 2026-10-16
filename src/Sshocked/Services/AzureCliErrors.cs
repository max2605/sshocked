using Sshocked.Models;

namespace Sshocked.Services;

internal static class AzureCliErrors
{
    public static void ThrowIfCertificateFailure(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0 && (IsCertificateFailure(result.StandardError) || IsCertificateFailure(result.StandardOutput)))
            throw new AzureCliCertificateException(operation, result.ExitCode, result.StandardError + Environment.NewLine + result.StandardOutput);
    }

    public static bool IsCertificateFailure(string output)
        => output.Contains("CERTIFICATE_VERIFY_FAILED", StringComparison.OrdinalIgnoreCase)
            || output.Contains("certificate verify failed", StringComparison.OrdinalIgnoreCase)
            || output.Contains("certificate verification failed", StringComparison.OrdinalIgnoreCase);
}
