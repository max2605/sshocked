using System.Text;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class CommandSessionPending(int number, Action<string> output, SessionWriter? recording)
{
    private readonly object gate = new();
    private readonly StringBuilder stdout = new();
    private readonly StringBuilder stderr = new();
    private readonly TaskCompletionSource<int> outputComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> errorComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Number { get; } = number;
    public Task Completion => Task.WhenAll(outputComplete.Task, errorComplete.Task);

    public void Append(string data, bool error)
    {
        if (data.Length == 0) return;
        lock (gate)
        {
            (error ? stderr : stdout).Append(data);
            recording?.Append(data);
            output(data);
        }
    }

    public bool Complete(string marker, bool error)
    {
        var separator = marker.IndexOf(':');
        if (separator < 1 || !int.TryParse(marker[..separator], out var sequence) || sequence != Number ||
            !int.TryParse(marker[(separator + 1)..], out var exitCode)) return false;
        (error ? errorComplete : outputComplete).TrySetResult(exitCode);
        return true;
    }

    public ProcessResult Result()
    {
        lock (gate) return new(outputComplete.Task.Result, stdout.ToString(), stderr.ToString());
    }
}
