using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class DockerSecretTestsScanProcess : IProcessService
{
    public List<string> Commands { get; } = [];
    public List<string> Inputs { get; } = [];

    public Task RunSsh(ServerHost host) => Task.CompletedTask;
    public Task RunSshInteractive(ServerHost host, string command)
    {
        Commands.Add(command);
        return Task.CompletedTask;
    }

    public Task<string> RunSshCommand(ServerHost host, string command)
    {
        Commands.Add(command);
        return Task.FromResult(Output(command));
    }

    public Task<ProcessResult> RunSshCommandWithInput(ServerHost host, string command, string input, CancellationToken ct = default)
    {
        Commands.Add(command);
        Inputs.Add(input);
        return Task.FromResult(new ProcessResult(0, Output(command), ""));
    }

    private static string Output(string command) => command.Contains("docker info", StringComparison.Ordinal) ? "{\"ServerVersion\":\"27.0.0\",\"OSType\":\"linux\",\"Architecture\":\"x86_64\",\"Containers\":0,\"ContainersRunning\":0}" : "";
}
