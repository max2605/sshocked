using System.Text;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class HookRemoteSessionCommand(IAuditService auditService, ILogger<HookExecutorService> logger)
{
    public string? Build(ServerHost host, GlobalHooks? globalHooks, HookContext context)
    {
        var postConnect = HookDefinition.GetOrderedHooks(HookPhase.PostConnect, host, globalHooks)
            .Where(hook => hook.Mode == HookExecutionMode.RemoteCommand).ToList();
        var preDisconnect = HookDefinition.GetOrderedHooks(HookPhase.PreDisconnect, host, globalHooks)
            .Where(hook => hook.Mode == HookExecutionMode.RemoteCommand).ToList();
        if (postConnect.Count == 0 && preDisconnect.Count == 0) return null;
        var command = new StringBuilder();
        foreach (var hook in postConnect) AppendRemoteHook(command, hook, HookPhase.PostConnect, host, context);
        command.AppendLine("\"${SHELL:-/bin/sh}\" -i");
        command.AppendLine("_sshocked_session_exit=$?");
        foreach (var hook in preDisconnect) AppendRemoteHook(command, hook, HookPhase.PreDisconnect, host, context);
        command.AppendLine("exit \"$_sshocked_session_exit\"");
        // The command is parsed by the remote POSIX shell even when sshocked runs on Windows.
        return command.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private void AppendRemoteHook(StringBuilder command, HookScript hook, HookPhase phase, ServerHost host, HookContext context)
    {
        HookDefinition.Validate(hook, phase);
        logger.LogInformation("Scheduling remote {Phase} hook for {Alias}; remote commands inherit the SSH terminal and do not enforce TimeoutSeconds", HookDefinition.PhaseName(phase), host.Alias);
        var block = new StringBuilder();
        foreach (var variable in HookDefinition.BuildEnvironment(host, phase, context, hook))
            block.Append("export ").Append(variable.Key).Append('=').Append(RemoteShell.Quote(variable.Value)).AppendLine();
        if (phase == HookPhase.PreDisconnect) block.AppendLine("export SSHOCKED_EXIT_CODE=\"$_sshocked_session_exit\"");
        block.Append("eval ").Append(RemoteShell.Quote(hook.Command! + (string.IsNullOrWhiteSpace(hook.Arguments) ? "" : " " + hook.Arguments)));
        if (hook.OutputMode == HookOutputMode.Suppress) block.Append(" >/dev/null 2>&1");
        block.AppendLine();
        block.AppendLine("_sshocked_hook_exit=$?");
        block.AppendLine("if [ \"$_sshocked_hook_exit\" -ne 0 ]; then");
        block.Append("  printf '%s\\n' ").Append(RemoteShell.Quote($"sshocked {HookDefinition.PhaseName(phase)} hook failed")).AppendLine(" >&2");
        if (!hook.ContinueOnError) block.AppendLine("  exit \"$_sshocked_hook_exit\"");
        block.AppendLine("fi");
        if (hook.Blocking) command.Append(block);
        else command.AppendLine("(").Append(block).AppendLine(") &");
        auditService.Record(new AuditEntry
        {
            EventType = HookConstants.ScheduledEvent,
            Category = HookConstants.AuditCategory,
            HostId = host.Id,
            Alias = host.Alias,
            Action = $"{HookDefinition.PhaseName(phase)} (RemoteCommand)",
            Success = true
        });
    }

}
