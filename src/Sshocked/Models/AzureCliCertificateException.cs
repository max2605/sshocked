namespace Sshocked.Models;

public sealed class AzureCliCertificateException(string operation, int exitCode, string standardError)
    : Exception(
        $"{operation} failed because Azure CLI could not verify the TLS certificate (CERTIFICATE_VERIFY_FAILED)." + Environment.NewLine +
        "This commonly occurs behind a corporate proxy with SSL/TLS inspection. Obtain your organization's trusted root/intermediate CA certificates and include them in a PEM CA bundle alongside the public CAs." + Environment.NewLine +
        "Set REQUESTS_CA_BUNDLE to that bundle's path in the shell that launches Sshocked, then retry:" + Environment.NewLine +
        "  PowerShell: $env:REQUESTS_CA_BUNDLE = 'C:\\certs\\ca-bundle.pem'" + Environment.NewLine +
        "  Command Prompt: set \"REQUESTS_CA_BUNDLE=C:\\certs\\ca-bundle.pem\"" + Environment.NewLine +
        "Azure CLI proxy/certificate documentation: https://learn.microsoft.com/en-us/cli/azure/use-azure-cli-successfully-troubleshooting#work-behind-a-proxy",
        new ProcessExecutionException(operation, exitCode, standardError))
{
    public int ExitCode { get; } = exitCode;
    public string StandardError { get; } = standardError;
}
