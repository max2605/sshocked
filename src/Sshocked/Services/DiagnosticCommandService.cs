using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class DiagnosticCommandService(IDiagnosticService diagnostics, IConsoleWriterService console) : IManagementCommandHandler
{
    public bool Handles(string command) => command.Equals("diagnostics", StringComparison.OrdinalIgnoreCase);

    public async Task Execute(string command, string[] arguments)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "preview";
        var options = new CommandOptions(arguments.Length == 0 ? [] : arguments[1..], ["server", "output"],
            ["include-paths", "include-command-text", "probe"]);
        if (options.Positionals.Count > 0 || action is not ("preview" or "export") || action == "preview" && options.Has("output") || action == "export" && !options.Has("output"))
            throw new ArgumentException("Use diagnostics preview|export [--server ID|ALIAS] [--probe] [--include-paths] [--include-command-text]; export requires --output FILE.");
        var report = await diagnostics.Create(options.Get("server"), new DiagnosticOptions(options.Has("include-paths"), options.Has("include-command-text"), options.Has("probe")));
        DiagnosticPresentation.Preview(report, console);
        if (action == "export")
        {
            diagnostics.Export(report, options.Get("output")!);
            console.WriteLine("Diagnostic report saved: " + Path.GetFullPath(options.Get("output")!));
        }
    }
}
