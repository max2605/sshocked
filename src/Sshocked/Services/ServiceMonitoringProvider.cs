using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ServiceMonitoringProvider(IMonitoringTransport transport) : IMonitoringProvider
{
    public string Key => "service";
    public string Kind => "service";
    public IReadOnlyList<MonitorOption> Options { get; } = [
        new("platform", "Target operating system", "linux", Choices: ["linux", "windows"]),
        new("name", "Exact systemd unit or Windows service name", Required: true),
        new("expected", "Expected state", "running", Choices: ["running", "stopped"]),
        new("severity", "State alarm severity", "warning", Choices: ["warning", "critical"]),
        new("resources", "Collect available service resources", "false", Choices: ["true", "false"]),
        new("allowSudo", "Allow configured noninteractive sudo on Linux", "false", Choices: ["true", "false"]),
        new("needrestart", "Enable installed needrestart in list/batch mode", "false", Choices: ["true", "false"])
    ];

    public void Validate(MonitorDefinition definition)
    {
        ProcessMonitorParsing.ValidatePlatform(definition);
        var name = MonitoringOptions.Get(definition, "name");
        ProcessMonitorParsing.ValidateText(name, "name", true);
        if (name.IndexOfAny(['*', '?', '[', ']']) >= 0) throw new ArgumentException("Service names must be exact, not wildcard patterns.");
        if (MonitoringOptions.Get(definition, "expected", "running") is not ("running" or "stopped")) throw new ArgumentException("expected must be running or stopped.");
        _ = MonitoringOptions.Boolean(definition, "resources");
        _ = MonitoringOptions.Boolean(definition, "allowSudo");
        _ = MonitoringOptions.Boolean(definition, "needrestart");
        if (MonitoringOptions.Get(definition, "platform", "linux") == "windows" &&
            (MonitoringOptions.Boolean(definition, "allowSudo") || MonitoringOptions.Boolean(definition, "needrestart")))
            throw new ArgumentException("allowSudo and needrestart apply only to Linux services.");
    }

    public async Task<MonitoringObservation> Collect(ServerHost host, MonitorDefinition definition,
        MonitoringRecord? previous, CancellationToken cancellationToken = default)
    {
        Validate(definition);
        var windows = MonitoringOptions.Get(definition, "platform", "linux") == "windows";
        var name = MonitoringOptions.Get(definition, "name");
        var command = windows ? WindowsCommand(name) : LinuxCommand(name, MonitoringOptions.Boolean(definition, "needrestart"));
        var result = await transport.Execute(host, command, new(definition.MaxOutputBytes, definition.TimeoutSeconds,
            MonitoringOptions.Boolean(definition, "allowSudo")), cancellationToken);
        MonitoringProbeResult.Ensure(result);
        return windows ? ParseWindows(result.StandardOutput, definition, previous) : ParseLinux(result.StandardOutput, definition, previous);
    }

    public static MonitoringObservation ParseLinux(string output, MonitorDefinition definition, MonitoringRecord? previous)
    {
        var properties = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains('=')).Select(line => line.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        string Get(string key) => properties.GetValueOrDefault(key, "");
        var load = Get("LoadState");
        var active = Get("ActiveState");
        var state = load switch
        {
            "not-found" => "Missing",
            "masked" => "Masked",
            _ => active switch
            {
                "active" => "Running",
                "inactive" => "Stopped",
                "failed" => "Failed",
                "activating" => "Starting",
                "deactivating" => "Stopping",
                "reloading" => "Reloading",
                _ => "Unknown"
            }
        };
        var observation = Create(definition, state, "systemd");
        observation.Snapshot.Metrics.AddRange([
            ProcessMonitorParsing.Metric("service.rawState", "Native state", active + "/" + Get("SubState"), "systemd"),
            ProcessMonitorParsing.Metric("service.loadState", "Load state", load, "systemd"),
            ProcessMonitorParsing.Metric("service.pid", "Main PID", ProcessMonitorParsing.Number(Get("MainPID")), "pid", "systemd"),
            ProcessMonitorParsing.Metric("service.exitCode", "Last exit code", ProcessMonitorParsing.Number(Get("ExecMainStatus")), "code", "systemd"),
            ProcessMonitorParsing.Metric("service.restarts", "Observed restart counter", ProcessMonitorParsing.Number(Get("NRestarts")), "count", "systemd"),
            ProcessMonitorParsing.Metric("service.result", "Last result", Get("Result"), "systemd")
        ]);
        var markerPresent = properties.ContainsKey("Markers");
        var restart = Get("Markers").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("needs-restart") ? "Required"
            : markerPresent ? "NotRequired" : "Unknown";
        var source = markerPresent ? "systemd Markers" : "No reliable restart signal";
        if (MonitoringOptions.Boolean(definition, "needrestart"))
        {
            if (Get("SSK_NEEDRESTART") == "success")
            {
                var matched = output.Split('\n').Where(line => line.StartsWith("NEEDRESTART-SVC:", StringComparison.Ordinal))
                    .Any(line => line["NEEDRESTART-SVC:".Length..].Trim().Equals(Get("Id"), StringComparison.Ordinal));
                restart = restart == "Required" || matched ? "Required" : "NotRequired";
                source = "needrestart list/batch";
            }
            else if (restart != "Required") { restart = "Unknown"; source = "needrestart unavailable or insufficient permissions"; }
        }
        Restart(observation, restart, source);
        var reload = Get("NeedDaemonReload") switch { "yes" => 1d, "no" => 0d, _ => (double?)null };
        observation.Snapshot.Metrics.Add(ProcessMonitorParsing.Metric("service.reloadRequired", "Unit configuration reload required", reload, "boolean", "systemd NeedDaemonReload"));
        if (reload is not null) observation.Conditions.Add(new("service.reload-required", "Unit configuration needs reload", reload == 1));
        var identity = Get("SSK_BOOT").Length > 0 && ProcessMonitorParsing.Number(Get("MainPID")) > 0 &&
            (Get("InvocationID").Length > 0 || ProcessMonitorParsing.Number(Get("ExecMainStartTimestampMonotonic")) > 0)
            ? Get("SSK_BOOT") + ":" + Get("InvocationID") + ":" + Get("MainPID") + ":" + Get("ExecMainStartTimestampMonotonic") : "";
        var time = ProcessMonitorParsing.Number(Get("SSK_TIME"));
        var cpu = ProcessMonitorParsing.Number(Get("CPUUsageNSec"));
        var cores = ProcessMonitorParsing.Number(Get("SSK_CORES"));
        Resources(observation, definition, previous, identity, time, cpu, 1_000_000_000,
            cores, ProcessMonitorParsing.Number(Get("MemoryCurrent")));
        return observation;
    }

    public static MonitoringObservation ParseWindows(string output, MonitorDefinition definition, MonitoringRecord? previous)
    {
        using var document = JsonDocument.Parse(output);
        var data = document.RootElement;
        var raw = ProcessMonitorParsing.Text(data, "state");
        var state = raw switch
        {
            "Running" => "Running",
            "Stopped" => "Stopped",
            "Start Pending" => "Starting",
            "Stop Pending" => "Stopping",
            "Paused" => "Paused",
            "Pause Pending" => "Pausing",
            "Continue Pending" => "Starting",
            "Missing" => "Missing",
            _ => "Unknown"
        };
        var observation = Create(definition, state, "Win32_Service");
        observation.Snapshot.Metrics.AddRange([
            ProcessMonitorParsing.Metric("service.displayName", "Display name", ProcessMonitorParsing.Text(data, "displayName"), "Win32_Service"),
            ProcessMonitorParsing.Metric("service.rawState", "Native state", raw, "Win32_Service"),
            ProcessMonitorParsing.Metric("service.startMode", "Start mode", ProcessMonitorParsing.Text(data, "startMode"), "Win32_Service"),
            ProcessMonitorParsing.Metric("service.pid", "Main PID", ProcessMonitorParsing.Number(data, "pid"), "pid", "Win32_Service"),
            ProcessMonitorParsing.Metric("service.exitCode", "Last exit code", ProcessMonitorParsing.Number(data, "exitCode"), "code", "Win32_Service")
        ]);
        Restart(observation, "Unknown", "No universal Windows service restart signal");
        Resources(observation, definition, previous, ProcessMonitorParsing.Text(data, "identity"), ProcessMonitorParsing.Number(data, "time"),
            ProcessMonitorParsing.Number(data, "cpuTicks"), 10_000_000, ProcessMonitorParsing.Number(data, "cores"), ProcessMonitorParsing.Number(data, "memory"));
        return observation;
    }

    private static MonitoringObservation Create(MonitorDefinition definition, string state, string source)
    {
        var expected = MonitoringOptions.Get(definition, "expected", "running");
        var known = state != "Unknown";
        return new()
        {
            Snapshot = new()
            {
                Quality = known ? MonitorDataQuality.Success : MonitorDataQuality.Partial,
                Message = $"Service state: {state}; expected {expected}.",
                Metrics = [ProcessMonitorParsing.Metric("service.state", "Service state", state, source)]
            },
            Conditions = [new("service.expected-state", "Service differs from expected state", known
                ? !state.Equals(expected, StringComparison.OrdinalIgnoreCase) : null, ProcessMonitorParsing.Severity(definition))]
        };
    }

    private static void Restart(MonitoringObservation observation, string restart, string source)
    {
        observation.Snapshot.Metrics.Add(ProcessMonitorParsing.Metric("service.restartRequirement", "Restart requirement", restart, source));
        if (restart != "Unknown") observation.Conditions.Add(new("service.restart-required", "Service restart required", restart == "Required"));
    }

    private static void Resources(MonitoringObservation observation, MonitorDefinition definition, MonitoringRecord? previous,
        string identity, double? time, double? cpu, double units, double? cores, double? memory)
    {
        observation.State["service.identity"] = identity;
        if (time is { } timestamp) observation.State["service.time"] = ProcessMonitorParsing.State(timestamp);
        if (cpu is { } counter) observation.State["service.cpu"] = ProcessMonitorParsing.State(counter);
        if (!MonitoringOptions.Boolean(definition, "resources")) return;
        var oldCpu = ProcessMonitorParsing.Previous(previous, "service.cpu");
        var oldTime = ProcessMonitorParsing.Previous(previous, "service.time");
        var same = previous?.State.GetValueOrDefault("service.identity") == identity && identity.Length > 0;
        double? percent = same && cpu >= oldCpu && time > oldTime && cores > 0
            ? Math.Clamp((cpu!.Value - oldCpu!.Value) / units / (time!.Value - oldTime!.Value) / cores!.Value * 100, 0, 100) : null;
        observation.Snapshot.Metrics.Add(ProcessMonitorParsing.Metric("service.cpuPercent", "CPU share of host capacity", percent, "%", "counter differences", MonitorDataQuality.Baseline));
        observation.Snapshot.Metrics.Add(ProcessMonitorParsing.Metric("service.memoryBytes", "Resident memory", memory, "bytes", "native service/process counters"));
    }

    private static string LinuxCommand(string name, bool needrestart) => MonitoringOptions.PosixScript($$"""
        command -v systemctl >/dev/null 2>&1 || exit 127
        [ -d /run/systemd/system ] || exit 127
        systemctl show --no-pager --property=Id --property=LoadState --property=ActiveState --property=SubState --property=MainPID --property=Result --property=ExecMainStatus --property=NRestarts --property=Markers --property=NeedDaemonReload --property=InvocationID --property=ExecMainStartTimestampMonotonic --property=CPUUsageNSec --property=MemoryCurrent -- {{RemoteShell.Quote(name)}}
        printf 'SSK_BOOT=%s\n' "$(cat /proc/sys/kernel/random/boot_id 2>/dev/null || true)"
        printf 'SSK_TIME=%s\n' "$(awk '{print $1}' /proc/uptime 2>/dev/null || true)"
        printf 'SSK_CORES=%s\n' "$(getconf _NPROCESSORS_ONLN 2>/dev/null || true)"
        {{(needrestart ? "if command -v needrestart >/dev/null 2>&1 && [ \"$(id -u)\" = 0 ]; then\n  if needrestart -b -r l; then printf 'SSK_NEEDRESTART=success\\n'; else printf 'SSK_NEEDRESTART=error\\n'; fi\nelse printf 'SSK_NEEDRESTART=unavailable\\n'; fi" : "")}}
        """);

    private static string WindowsCommand(string name) => MonitoringOptions.PowerShellScript($$"""
        $name={{ProcessMonitorParsing.PowerShellQuote(name)}};
        $s=Get-CimInstance Win32_Service | Where-Object { $_.Name -ieq $name };
        if (!$s) { @{state='Missing'} | ConvertTo-Json -Compress; exit 0 };
        $p=$null; if ($s.ProcessId -gt 0) { try { $p=Get-CimInstance Win32_Process -Filter ("ProcessId="+$s.ProcessId) } catch {} };
        $boot=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o');
        @{state=$s.State;displayName=$s.DisplayName;startMode=$s.StartMode;pid=$s.ProcessId;exitCode=$s.ExitCode;identity=$(if($p -and $p.CreationDate){$boot+':'+$s.ProcessId+':'+$p.CreationDate.ToUniversalTime().ToString('o')}else{$null});time=([Diagnostics.Stopwatch]::GetTimestamp()/[double][Diagnostics.Stopwatch]::Frequency);cores=[Environment]::ProcessorCount;cpuTicks=$(if($p){[double]$p.KernelModeTime+[double]$p.UserModeTime}else{$null});memory=$(if($p){[double]$p.WorkingSetSize}else{$null})} | ConvertTo-Json -Compress
        """);
}
