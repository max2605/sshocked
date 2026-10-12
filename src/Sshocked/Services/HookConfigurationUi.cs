using System.Text.RegularExpressions;
using Spectre.Console;
using Sshocked.Models;

namespace Sshocked.Services;

public static class HookConfigurationUi
{
    public static ConnectionHooks? Configure(ConnectionHooks? current)
    {
        var hooks = current ?? new ConnectionHooks();
        ConfigurePhases(hooks);
        return hooks.PreConnect is null && hooks.PostConnect is null && hooks.PreDisconnect is null && hooks.PostDisconnect is null
            ? null : hooks;
    }

    public static GlobalHooks ConfigureGlobal(GlobalHooks current)
    {
        ConfigurePhases(current);
        current.IncludeTags = PromptTags("Include tags (empty applies to every host)", current.IncludeTags);
        current.ExcludeTags = PromptTags("Exclude tags (exclusions take precedence)", current.ExcludeTags);
        return current;
    }

    private static void ConfigurePhases(ConnectionHooks hooks)
    {
        while (true)
        {
            var choices = Enum.GetValues<HookPhase>().Select(phase => new HookChoice(phase, Describe(Get(hooks, phase)))).ToList();
            choices.Add(new(null, "Back"));
            var choice = AnsiConsole.Prompt(new SelectionPrompt<HookChoice>().Title("[green]Connection hooks[/]")
                .UseConverter(item => item.Phase is { } phase ? $"{HookExecutorService.PhaseName(phase)}: {Markup.Escape(item.Description)}" : item.Description)
                .AddChoices(choices));
            if (choice.Phase is not { } selectedPhase) return;
            var current = Get(hooks, selectedPhase);
            var action = AnsiConsole.Prompt(new SelectionPrompt<string>().Title($"[green]{HookExecutorService.PhaseName(selectedPhase)}[/]")
                .AddChoices("Configure", "Remove", "Cancel"));
            if (action == "Remove") Set(hooks, selectedPhase, null);
            else if (action == "Configure") Set(hooks, selectedPhase, ConfigureScript(selectedPhase, current));
        }
    }

    private static HookScript ConfigureScript(HookPhase phase, HookScript? current)
    {
        var choices = new List<string> { "Local shell command", "Local script file", "Inline local script" };
        if (phase is HookPhase.PostConnect or HookPhase.PreDisconnect)
            choices.InsertRange(0, ["Remote command", "Change remote directory"]);
        var selection = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[green]Hook action[/]").AddChoices(choices));
        var hook = new HookScript
        {
            Environment = current is null ? [] : new Dictionary<string, string>(current.Environment)
        };
        switch (selection)
        {
            case "Remote command":
                hook.Mode = HookExecutionMode.RemoteCommand;
                hook.Command = PromptText("Remote command", current?.Command);
                break;
            case "Change remote directory":
                hook.Mode = HookExecutionMode.RemoteCommand;
                var path = PromptText("Remote directory", "~");
                hook.Command = "cd " + RemoteShell.QuotePath(path);
                break;
            case "Local script file":
                hook.Mode = HookExecutionMode.LocalScript;
                hook.ScriptPath = AnsiConsole.Prompt(new TextPrompt<string>("[green]Script file[/]:")
                    .DefaultValue(current?.ScriptPath ?? string.Empty)
                    .Validate(path => File.Exists(Environment.ExpandEnvironmentVariables(path))
                        ? ValidationResult.Success() : ValidationResult.Error("[red]The script file does not exist.[/]")));
                break;
            case "Inline local script":
                hook.Mode = HookExecutionMode.InlineScript;
                hook.InlineScript = PromptText(OperatingSystem.IsWindows() ? "Inline PowerShell script" : "Inline POSIX shell script", current?.InlineScript);
                break;
            default:
                hook.Mode = HookExecutionMode.ShellCommand;
                hook.Command = PromptText(OperatingSystem.IsWindows() ? "Local PowerShell command" : "Local shell command", current?.Command);
                break;
        }
        hook.Arguments = AnsiConsole.Prompt(new TextPrompt<string>("[green]Arguments[/] (optional; local script args accept ${SSHOCKED_HOST_ALIAS}):")
            .DefaultValue(current?.Arguments ?? string.Empty).AllowEmpty());
        hook.Enabled = AnsiConsole.Confirm("Enable this hook?", current?.Enabled ?? true);
        hook.Blocking = AnsiConsole.Confirm("Wait for hook completion?", current?.Blocking ?? true);
        hook.ContinueOnError = AnsiConsole.Confirm("Continue the connection when this hook fails?", current?.ContinueOnError ?? false);
        if (hook.Mode == HookExecutionMode.RemoteCommand)
        {
            hook.TimeoutSeconds = current?.TimeoutSeconds ?? 30;
            AnsiConsole.MarkupLine("[yellow]Remote hooks run in the session shell. Per-hook timeout is supported for local hooks only.[/]");
            AnsiConsole.MarkupLine("[grey]Remote cleanup runs after the interactive shell exits; disconnecting the network can prevent it.[/]");
            if (!hook.Blocking) AnsiConsole.MarkupLine("[yellow]A background remote hook cannot change the session's directory or environment.[/]");
        }
        else
            hook.TimeoutSeconds = AnsiConsole.Prompt(new TextPrompt<int>("[green]Timeout in seconds[/]:")
                .DefaultValue(current?.TimeoutSeconds ?? 30).Validate(value => value is >= 1 and <= 86400
                    ? ValidationResult.Success() : ValidationResult.Error("[red]Use 1 to 86400 seconds.[/]")));
        if (!hook.Blocking) AnsiConsole.MarkupLine("[yellow]Background failures are logged; they cannot abort an already started connection.[/]");
        hook.OutputMode = AnsiConsole.Prompt(new SelectionPrompt<HookOutputMode>().Title("[green]Hook output[/]")
            .AddChoices(Enum.GetValues<HookOutputMode>()).DefaultValue(current?.OutputMode ?? HookOutputMode.Log));
        if (hook.Mode == HookExecutionMode.RemoteCommand && hook.OutputMode is HookOutputMode.Log or HookOutputMode.Both)
            AnsiConsole.MarkupLine("[yellow]Remote output appears in the SSH terminal; only the scheduling event is written to the local audit log.[/]");
        if (AnsiConsole.Confirm("Configure custom environment variables?", false)) ConfigureEnvironment(hook.Environment);
        HookExecutorService.Validate(hook, phase);
        return hook;
    }

    private static void ConfigureEnvironment(Dictionary<string, string> environment)
    {
        while (true)
        {
            var action = AnsiConsole.Prompt(new SelectionPrompt<string>().Title($"[green]Environment ({environment.Count} variables)[/]")
                .AddChoices("Add or update", "Remove", "Done"));
            if (action == "Done") return;
            if (action == "Remove")
            {
                if (environment.Count == 0) continue;
                var key = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Variable to remove").AddChoices(environment.Keys));
                environment.Remove(key);
                continue;
            }
            var name = AnsiConsole.Prompt(new TextPrompt<string>("[green]Variable name[/]:").Validate(value =>
                Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*$") && !value.StartsWith("SSHOCKED_", StringComparison.Ordinal)
                    ? ValidationResult.Success() : ValidationResult.Error("[red]Use letters, digits, and underscores. SSHOCKED_ is reserved.[/]")));
            var value = AnsiConsole.Prompt(new TextPrompt<string>("[green]Value[/]:").AllowEmpty());
            environment[name] = value;
        }
    }

    private static string PromptText(string label, string? defaultValue) => AnsiConsole.Prompt(
        new TextPrompt<string>($"[green]{Markup.Escape(label)}[/]:").DefaultValue(defaultValue ?? string.Empty)
            .Validate(value => !string.IsNullOrWhiteSpace(value) && !value.Contains('\0')
                ? ValidationResult.Success() : ValidationResult.Error("[red]Enter a command or path.[/]")));

    private static List<string> PromptTags(string label, List<string> current) => AnsiConsole.Prompt(
        new TextPrompt<string>($"[green]{Markup.Escape(label)}[/] (comma-separated):")
            .DefaultValue(string.Join(", ", current)).AllowEmpty())
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string Describe(HookScript? hook) => hook is null ? "not configured"
        : $"{hook.Mode} ({(hook.Enabled ? "enabled" : "disabled")})";

    private static HookScript? Get(ConnectionHooks hooks, HookPhase phase) => phase switch
    {
        HookPhase.PreConnect => hooks.PreConnect, HookPhase.PostConnect => hooks.PostConnect,
        HookPhase.PreDisconnect => hooks.PreDisconnect, HookPhase.PostDisconnect => hooks.PostDisconnect,
        _ => null
    };

    private static void Set(ConnectionHooks hooks, HookPhase phase, HookScript? script)
    {
        switch (phase)
        {
            case HookPhase.PreConnect: hooks.PreConnect = script; break;
            case HookPhase.PostConnect: hooks.PostConnect = script; break;
            case HookPhase.PreDisconnect: hooks.PreDisconnect = script; break;
            case HookPhase.PostDisconnect: hooks.PostDisconnect = script; break;
        }
    }

    private sealed record HookChoice(HookPhase? Phase, string Description);
}
