using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MacroService(IConfigRepository repository, IProcessService process, IAuditService audit) : IMacroService
{
    public IReadOnlyList<MacroDefinition> List() => repository.Load().Macros.OrderBy(macro => macro.Name, StringComparer.OrdinalIgnoreCase)
        .Select(macro => macro.Copy()).ToList();

    public MacroDefinition Find(string nameOrId)
    {
        var matches = List().Where(macro => macro.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            macro.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : throw new ArgumentException(matches.Count == 0 ? "Macro not found." : "Macro name is ambiguous; use its ID.");
    }

    public MacroDefinition Save(MacroRecording recording, string name)
    {
        if (recording.IsRecording) throw new InvalidOperationException("Stop recording before saving the macro.");
        return CreateCore(name, recording.Commands, recording.Host.Id, MacroEventNames.Saved, recording.Host.Alias);
    }

    public MacroDefinition Create(string name, IReadOnlyList<string> commands) =>
        CreateCore(name, commands, null, MacroEventNames.Created);

    public MacroDefinition Duplicate(MacroDefinition original, string name, IReadOnlyList<string> commands) =>
        CreateCore(name, commands, original.RecordedOnHostId, MacroEventNames.Duplicated);

    public MacroDefinition Update(MacroDefinition original, string name, IReadOnlyList<string> commands)
    {
        var expected = original.Copy();
        var macro = expected.Copy();
        macro.Name = MacroCommandPolicy.NormalizeName(name);
        macro.Commands = commands.ToList();
        _ = MacroScriptBuilder.Build(macro);
        repository.Update(config =>
        {
            var index = config.Macros.FindIndex(existing => existing.Id == expected.Id);
            if (index < 0 || !config.Macros[index].Matches(expected))
                throw new InvalidOperationException("This macro was changed or deleted elsewhere. Your draft was not saved. Discard it and reopen the macro to load the current version.");
            RequireUniqueName(config.Macros, macro.Name, expected.Id);
            config.Macros[index] = macro.Copy();
        });
        audit.Record(new() { Category = "config", EventType = MacroEventNames.Updated, HostId = macro.RecordedOnHostId, Action = macro.Name });
        return macro;
    }

    private MacroDefinition CreateCore(string name, IReadOnlyList<string> commands, string? recordedOnHostId,
        string eventType, string? alias = null)
    {
        var macro = new MacroDefinition
        {
            Name = MacroCommandPolicy.NormalizeName(name),
            Commands = commands.ToList(),
            RecordedOnHostId = recordedOnHostId
        };
        _ = MacroScriptBuilder.Build(macro);
        repository.Update(config =>
        {
            RequireUniqueName(config.Macros, macro.Name);
            config.Macros.Add(macro.Copy());
        });
        audit.Record(new() { Category = "config", EventType = eventType, HostId = recordedOnHostId, Alias = alias, Action = macro.Name });
        return macro;
    }

    private static void RequireUniqueName(IReadOnlyList<MacroDefinition> macros, string name, string? exceptId = null)
    {
        if (macros.Any(existing => existing.Id != exceptId && existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A macro with that name already exists. Choose another name.");
    }

    public void Delete(string id)
    {
        repository.Update(config =>
        {
            if (config.Macros.RemoveAll(macro => macro.Id == id) == 0) throw new ArgumentException("Macro not found.");
        });
        audit.Record(new() { Category = "config", EventType = MacroEventNames.Deleted, Action = id });
    }

    public Task<IReadOnlyList<MacroTargetResult>> Run(MacroDefinition macro, IReadOnlyList<ServerHost> targets,
        Action<string> progress, CancellationToken cancellationToken = default) => RunCore(macro, targets, progress, false, cancellationToken);

    public Task<IReadOnlyList<MacroTargetResult>> RunNonInteractive(MacroDefinition macro, IReadOnlyList<ServerHost> targets,
        Action<string> progress, CancellationToken cancellationToken = default) => RunCore(macro, targets, progress, true, cancellationToken);

    private async Task<IReadOnlyList<MacroTargetResult>> RunCore(MacroDefinition definition, IReadOnlyList<ServerHost> targets,
        Action<string> progress, bool nonInteractive, CancellationToken cancellationToken)
    {
        var macro = definition.Copy();
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
                foreach (var command in macro.Commands) MacroCommandPolicy.Validate(command, host);
                var execution = await process.RunSshCommandStreamingValidated(host, script,
                    prepared => { foreach (var command in macro.Commands) MacroCommandPolicy.Validate(command, prepared); },
                    progress, nonInteractive, cancellationToken);
                result = new(host.Id, host.Alias, execution.ExitCode == 0, execution.ExitCode, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { result = new(host.Id, host.Alias, false, null, AuditRedactor.Redact(exception.Message)); }
            results.Add(result);
            audit.Record(new()
            {
                Category = "command",
                EventType = MacroEventNames.Executed,
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
