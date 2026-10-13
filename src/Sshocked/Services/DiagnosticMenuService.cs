using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class DiagnosticMenuService(IDiagnosticService diagnostics, IConfigRepository repository, IPathInputService paths,
    IMenuFactory menus, IConsoleWriterService console, IConsoleHelperService helper) : IDiagnosticMenuService
{
    public async Task Show()
    {
        while (await menus.RunMenu("[bold]Local diagnostics[/]", [
            new("Create diagnostic report", 'C', executeAsync: Create),
            new("Back", 'B', actionType: MenuActionType.Back)
        ])) { }
    }

    private async Task Create()
    {
        var hosts = repository.Load().Hosts;
        var choices = hosts.Select(host => $"{host.Alias} ({host.Id})").Prepend("Application only").Append("Back");
        var selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Diagnostic target:").UseConverter(Markup.Escape).AddChoices(choices));
        if (selected == "Back") return;
        var host = hosts.FirstOrDefault(item => selected == $"{item.Alias} ({item.Id})");
        var includePaths = AnsiConsole.Confirm("Include paths if audit privacy settings permit it?", false);
        var includeCommands = AnsiConsole.Confirm("Include sanitized command text if audit privacy settings permit it?", false);
        var probe = host is not null && AnsiConsole.Confirm("Run a TCP port probe? This does not test SSH authentication.", false);
        var report = await diagnostics.Create(host?.Id, new DiagnosticOptions(includePaths, includeCommands, probe));
        DiagnosticPresentation.Preview(report, console);
        helper.WaitForKey();
        if (paths.Prompt("Destination report file (empty cancels save)") is { } path)
        {
            diagnostics.Export(report, path);
            console.WriteLine("Diagnostic report saved: " + Path.GetFullPath(path));
            helper.WaitForKey();
        }
    }
}
