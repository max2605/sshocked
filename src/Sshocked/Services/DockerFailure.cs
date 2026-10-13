using Sshocked.Models;

namespace Sshocked.Services;

internal static class DockerFailure
{
    public static string Message(ProcessResult result)
    {
        var error = result.StandardError;
        if (result.ExitCode == 255) return "SSH connection or authentication failed. Update the server credentials and check connectivity.";
        if (error.Contains("incorrect password", StringComparison.OrdinalIgnoreCase) || error.Contains("authentication failure", StringComparison.OrdinalIgnoreCase))
            return "Sudo authentication failed. Update the server's sudo password or secret reference.";
        if (error.Contains("sudo", StringComparison.OrdinalIgnoreCase) && (error.Contains("password", StringComparison.OrdinalIgnoreCase) || error.Contains("not allowed", StringComparison.OrdinalIgnoreCase)))
            return "Docker requires sudo access. Configure the server's sudo credential or a permitted noninteractive Docker command.";
        if (result.ExitCode == 127 || error.Contains("docker: not found", StringComparison.OrdinalIgnoreCase)) return "Docker is not installed or is not on the remote PATH.";
        if (error.Contains("permission denied", StringComparison.OrdinalIgnoreCase)) return "Docker access was denied. Check the server's Docker and sudo permissions.";
        if (error.Contains("connect to the Docker daemon", StringComparison.OrdinalIgnoreCase) || error.Contains("Is the docker daemon running", StringComparison.OrdinalIgnoreCase))
            return "The Docker daemon is unavailable. Check the daemon and the selected user's Docker endpoint.";
        if (error.Contains("No such container", StringComparison.OrdinalIgnoreCase)) return "The selected container no longer exists. Rescan the server before retrying.";
        return $"Docker command failed (exit code {result.ExitCode}). Check the container state and remote Docker permissions.";
    }
}
