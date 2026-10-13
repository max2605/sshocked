using Sshocked.Interfaces;
using Sshocked.Services;

namespace Sshocked.Models;

public sealed class MacroDraft(MacroDefinition? source = null, bool duplicate = false, MacroTemplate? template = null)
{
    private readonly MacroDefinition? original = source?.Copy();
    private readonly List<string> commands = (source?.Commands ?? template?.Commands ?? []).ToList();
    public string Name { get; private set; } = duplicate ? string.Empty : source?.Name ?? string.Empty;
    public IReadOnlyList<string> Commands => commands.AsReadOnly();
    public bool HasChanges => original is null || duplicate
        ? Name.Length > 0 || commands.Count > 0
        : Name != original.Name || !commands.SequenceEqual(original.Commands, StringComparer.Ordinal);

    public void SetName(string name) => Name = MacroCommandPolicy.NormalizeName(name);

    public void Add(string command)
    {
        MacroCommandPolicy.Validate(command);
        if (commands.Count >= MacroCommandPolicy.MaximumCommands)
            throw new ArgumentException($"A macro cannot contain more than {MacroCommandPolicy.MaximumCommands} commands.");
        commands.Add(command);
    }

    public void Edit(int index, string command)
    {
        MacroCommandPolicy.Validate(command);
        commands[index] = command;
    }

    public void Remove(int index) => commands.RemoveAt(index);

    public void Move(int index, int destination)
    {
        if (index < 0 || index >= commands.Count || destination < 0 || destination >= commands.Count)
            throw new ArgumentOutOfRangeException(nameof(destination), "Select a command within the sequence.");
        var command = commands[index];
        commands.RemoveAt(index);
        commands.Insert(destination, command);
    }

    public MacroDefinition Save(IMacroService macros) => original is null ? macros.Create(Name, commands)
        : duplicate ? macros.Duplicate(original, Name, commands) : macros.Update(original, Name, commands);
}
