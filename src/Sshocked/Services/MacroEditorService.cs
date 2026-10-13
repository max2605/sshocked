using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MacroEditorService(IMacroService macros, IMenuFactory menus, IConsoleWriterService console,
    IConsoleHelperService helper, IMacroEditorInputService? input = null) : IMacroEditorService
{
    private readonly IMacroEditorInputService prompts = input ?? new MacroEditorInputService(console);

    public Task<MacroDefinition?> Create() => Show(new MacroDraft(), MacroEditorLabels.Create);
    public Task<MacroDefinition?> Edit(MacroDefinition original) => Show(new MacroDraft(original), MacroEditorLabels.Edit);
    public Task<MacroDefinition?> Duplicate(MacroDefinition original) => Show(new MacroDraft(original, duplicate: true), MacroEditorLabels.Duplicate);

    public async Task<MacroDefinition?> CreateFromTemplate()
    {
        MacroTemplate? selected = null;
        var entries = MacroTemplates.All.Select(template => new MenuEntry(Markup.Escape(template.Name),
            executeAsync: () => { selected = template; return Task.CompletedTask; })).ToList();
        entries.Add(new(MacroEditorLabels.Back, 'B', actionType: MenuActionType.Back));
        while (selected is null)
            if (!await menus.RunMenu("[bold]Macro templates[/]", entries)) return null;
        console.WriteLine(selected.Description);
        helper.WaitForKey();
        return await Show(new MacroDraft(template: selected), MacroEditorLabels.FromTemplate);
    }

    private async Task<MacroDefinition?> Show(MacroDraft draft, string title)
    {
        MacroDefinition? saved = null;
        while (saved is null)
        {
            var entries = new List<MenuEntry>
            {
                new(MacroEditorLabels.Name, 'N', executeAsync: () => Change(() =>
                { if (prompts.Prompt("Macro name", draft.Name) is { } name) draft.SetName(name); })),
                new(MacroEditorLabels.Add, 'A', executeAsync: () => Change(() =>
                { if (prompts.Prompt("Command") is { } command) draft.Add(command); })),
                new(MacroEditorLabels.EditCommand, 'E', executeAsync: () => ChangeStep(draft, MacroEditorLabels.EditCommand)),
                new(MacroEditorLabels.DeleteCommand, 'D', executeAsync: () => ChangeStep(draft, MacroEditorLabels.DeleteCommand)),
                new(MacroEditorLabels.MoveUp, 'U', executeAsync: () => ChangeStep(draft, MacroEditorLabels.MoveUp)),
                new(MacroEditorLabels.MoveDown, 'O', executeAsync: () => ChangeStep(draft, MacroEditorLabels.MoveDown)),
                new(MacroEditorLabels.View, 'V', executeAsync: () =>
                { Render(draft); helper.WaitForKey(); return Task.CompletedTask; }),
                new(MacroEditorLabels.Save, 'S', executeAsync: () => { saved = TrySave(draft); return Task.CompletedTask; }),
                new(MacroEditorLabels.Back, 'B', actionType: MenuActionType.Back)
            };
            if (await menus.RunMenu($"[bold]{title}[/]", entries, () => Render(draft))) continue;
            if (!draft.HasChanges) return null;
            var discard = false;
            await menus.RunMenu("[bold]Unsaved macro changes[/]", [
                new(MacroEditorLabels.Save, 'S', executeAsync: () => { saved = TrySave(draft); return Task.CompletedTask; }),
                new(MacroEditorLabels.Discard, 'D', executeAsync: () => { discard = true; return Task.CompletedTask; }),
                new(MacroEditorLabels.Back, 'B', actionType: MenuActionType.Back)
            ]);
            if (discard) return null;
        }
        return saved;
    }

    private async Task ChangeStep(MacroDraft draft, string action)
    {
        if (draft.Commands.Count == 0) { console.WriteLine("Add a command first."); helper.WaitForKey(); return; }
        int? selected = null;
        var entries = draft.Commands.Select((command, index) => new MenuEntry(StepLabel(command, index),
            executeAsync: () => { selected = index; return Task.CompletedTask; })).ToList();
        entries.Add(new(MacroEditorLabels.Back, 'B', actionType: MenuActionType.Back));
        while (selected is null)
            if (!await menus.RunMenu($"[bold]{action}[/]", entries)) return;
        var step = selected.Value;
        await Change(() =>
        {
            if (action == MacroEditorLabels.EditCommand)
            {
                if (prompts.Prompt("Command", draft.Commands[step]) is { } command) draft.Edit(step, command);
            }
            else if (action == MacroEditorLabels.DeleteCommand) draft.Remove(step);
            else draft.Move(step, step + (action == MacroEditorLabels.MoveUp ? -1 : 1));
        });
    }

    private Task Change(Action change)
    {
        try { change(); }
        catch (ArgumentException error) { ShowError(error); }
        return Task.CompletedTask;
    }

    private MacroDefinition? TrySave(MacroDraft draft)
    {
        try
        {
            var saved = draft.Save(macros);
            console.WriteLine($"Macro '{saved.Name}' saved ({saved.Commands.Count} commands). No commands were executed.");
            return saved;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        { ShowError(error); return null; }
    }

    private void ShowError(Exception error)
    {
        console.WriteLine("Error: " + AuditRedactor.Redact(error.Message));
        helper.WaitForKey();
    }

    private void Render(MacroDraft draft)
    {
        if (AnsiConsole.Profile.Capabilities.Interactive) AnsiConsole.Clear();
        console.WriteLine($"Name: {(draft.Name.Length == 0 ? "(not set)" : draft.Name)}{(draft.HasChanges ? " *" : "")}");
        console.WriteLine("Macro text is stored locally. Keep secrets in remote credential references; arbitrary secrets cannot be detected reliably.");
        console.WriteLine("Commands run later in a noninteractive POSIX shell. Use Connect for password prompts and interactive programs.");
        if (draft.Commands.Count == 0) console.WriteLine("No commands yet.");
        for (var index = 0; index < draft.Commands.Count; index++) console.WriteLine($"{index + 1}. {draft.Commands[index]}");
    }

    private static string StepLabel(string command, int index)
    {
        var preview = command.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        if (preview.Length > 120) preview = preview[..120] + "…";
        return $"{index + 1}. {Markup.Escape(preview)}";
    }
}
