using Spectre.Console;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class DockerConsoleProgress : IProgress<DockerBatchProgress>
{
    private readonly object gate = new();

    public void Report(DockerBatchProgress progress)
    {
        lock (gate)
        {
            var text = progress.State is null ? $"Starting: {progress.Target}" : $"{progress.Completed}/{progress.Total}: {progress.Target} — {progress.State}";
            AnsiConsole.WriteLine(text);
        }
    }
}
