using Sshocked.Models;

namespace Sshocked.Interfaces;

public interface IManagedCommandSession : IAsyncDisposable
{
    void ValidateRecordingCommand(string command) => Services.MacroCommandPolicy.Validate(command);
    Task<ProcessResult> Execute(string command, Action<string> output, CancellationToken cancellationToken = default);
}
