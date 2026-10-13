using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MacroService(IConfigRepository repository, IProcessService process, IAuditService audit) : IMacroService
{
    public IReadOnlyList<MacroDefinition> List() => repository.Load().Macros.OrderBy(macro => macro.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public MacroDefinition Find(string nameOrId)
    {
        var matches = List().Where(macro => macro.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            macro.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : throw new ArgumentException(matches.Count == 0 ? "Macro not found." : "Macro name is ambiguous; use its ID.");
    }

    public MacroDefinition Save(MacroRecording recording, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new ArgumentException("Use a macro name of 1 to 80 characters.");
        if (recording.IsRecording) throw new InvalidOperationException("Stop recording before saving the macro.");
        var macro = new MacroDefinition { Name = name, Commands = recording.Commands.ToList(), RecordedOnHostId = recording.Host.Id };
        _ = MacroScriptBuilder.Build(macro);
        repository.Update(config =>
        {
            if (config.Macros.Any(existing => existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A macro with that name already exists. Choose another name.");
            config.Macros.Add(macro);
        });
        audit.Record(new() { Category = "config", EventType = "macro.saved", HostId = recording.Host.Id, Alias = recording.Host.Alias, Action = macro.Name });
        return macro;
    }

    public void Delete(string id)
    {
        repository.Update(config =>
        {
            if (config.Macros.RemoveAll(macro => macro.Id == id) == 0) throw new ArgumentException("Macro not found.");
        });
        audit.Record(new() { Category = "config", EventType = "macro.deleted", Action = id });
    }

    public async Task<IReadOnlyList<MacroTargetResult>> Run(MacroDefinition macro, IReadOnlyList<ServerHost> targets,
        Action<string> progress, CancellationToken cancellationToken = default)
    {
        var script = MacroScriptBuilder.Build(macro);
        var hosts = targets.DistinctBy(host => host.Id).ToList();
        if (hosts.Count == 0) throw new ArgumentException("Select at least one server.");
        var results = new List<MacroTargetResult>();
        foreach (var host in hosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress($"[{results.Count + 1}/{hosts.Count}] {host.Alias}: running macro '{macro.Name}' ({macro.Commands.Count} commands)\n");
            MacroTargetResult result;
            try
            {
                var execution = await process.RunSshCommandStreaming(host, script, progress, cancellationToken);
                result = new(host.Id, host.Alias, execution.ExitCode == 0, execution.ExitCode, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { result = new(host.Id, host.Alias, false, null, AuditRedactor.Redact(exception.Message)); }
            results.Add(result);
            audit.Record(new()
            {
                Category = "command",
                EventType = "macro.executed",
                HostId = host.Id,
                Alias = host.Alias,
                Action = macro.Name,
                Success = result.Success,
                ExitCode = result.ExitCode,
                ErrorMessage = result.Error
            });
            progress($"\n[{results.Count}/{hosts.Count}] {host.Alias}: {(result.Success ? "completed" : "FAILED")}; exit {result.ExitCode?.ToString() ?? "unavailable"}{(result.Error is null ? "" : "; " + result.Error)}\n");
        }
        return results;
    }
}
