using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ProcessMonitoringProvider(IMonitoringTransport transport) : IMonitoringProvider
{
    public string Key => "process";
    public string Kind => "process";
    public IReadOnlyList<MonitorOption> Options { get; } = [
        new("platform", "Target operating system", "linux", Choices: ["linux", "windows"]),
        new("name", "Exact process name (Windows includes .exe)", Required: true),
        new("path", "Optional exact executable path"), new("user", "Optional owner name or Linux UID"),
        new("minCount", "Minimum expected instances", "1"), new("maxCount", "Maximum expected instances", "100000"),
        new("severity", "Count alarm severity", "warning", Choices: ["warning", "critical"]),
        new("resources", "Collect CPU and resident memory", "false", Choices: ["true", "false"]),
        new("allowSudo", "Allow configured noninteractive sudo on Linux", "false", Choices: ["true", "false"])
    ];

    public void Validate(MonitorDefinition definition)
    {
        ProcessMonitorParsing.ValidatePlatform(definition);
        ProcessMonitorParsing.ValidateText(MonitoringOptions.Get(definition, "name"), "name", true);
        ProcessMonitorParsing.ValidateText(MonitoringOptions.Get(definition, "path"), "path");
        ProcessMonitorParsing.ValidateText(MonitoringOptions.Get(definition, "user"), "user");
        var minimum = MonitoringOptions.Integer(definition, "minCount", 1, 0, 100000);
        var maximum = MonitoringOptions.Integer(definition, "maxCount", 100000, 0, 100000);
        if (minimum > maximum) throw new ArgumentException("minCount cannot exceed maxCount.");
        _ = MonitoringOptions.Boolean(definition, "resources");
        _ = MonitoringOptions.Boolean(definition, "allowSudo");
        if (MonitoringOptions.Get(definition, "platform", "linux") == "windows" && MonitoringOptions.Boolean(definition, "allowSudo"))
            throw new ArgumentException("allowSudo applies only to Linux processes.");
    }

    public async Task<MonitoringObservation> Collect(ServerHost host, MonitorDefinition definition,
        MonitoringRecord? previous, CancellationToken cancellationToken = default)
    {
        Validate(definition);
        var windows = MonitoringOptions.Get(definition, "platform", "linux") == "windows";
        var command = windows ? WindowsCommand(definition) : LinuxCommand(definition);
        var result = await transport.Execute(host, command, MonitoringProbeResult.Options(definition, MonitoringOptions.Boolean(definition, "allowSudo")), cancellationToken);
        MonitoringProbeResult.Ensure(result);
        return Build(windows ? ParseWindows(result.StandardOutput) : ParseLinux(result.StandardOutput), definition, previous);
    }

    public static ProcessProbeSample ParseLinux(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r').Split('\t')).ToList();
        var metadata = lines.FirstOrDefault(fields => fields.Length == 6 && fields[0] == "META")
            ?? throw new InvalidDataException("The /proc sample header is missing.");
        var processes = new List<ProcessProbeInstance>();
        var complete = metadata[5] == "1";
        foreach (var fields in lines.Where(fields => fields[0] == "PROC"))
        {
            if (fields.Length != 5 || ProcessMonitorParsing.Number(fields[1]) is null || ProcessMonitorParsing.Number(fields[2]) is null)
            { complete = false; continue; }
            processes.Add(new(fields[1], fields[2], ProcessMonitorParsing.Number(fields[3]), ProcessMonitorParsing.Number(fields[4])));
        }
        return new(metadata[1], ProcessMonitorParsing.Number(metadata[2]), ProcessMonitorParsing.Number(metadata[3]),
            ProcessMonitorParsing.Number(metadata[4]), complete, processes.DistinctBy(process => process.Pid).ToList());
    }

    public static ProcessProbeSample ParseWindows(string output)
    {
        using var document = JsonDocument.Parse(output);
        var data = document.RootElement;
        var processes = new List<ProcessProbeInstance>();
        var complete = ProcessMonitorParsing.Boolean(data, "complete");
        if (data.TryGetProperty("processes", out var entries) && entries.ValueKind == JsonValueKind.Array)
            foreach (var entry in entries.EnumerateArray())
            {
                var pid = ProcessMonitorParsing.Text(entry, "pid");
                if (ProcessMonitorParsing.Number(pid) is null) { complete = false; continue; }
                processes.Add(new(pid, ProcessMonitorParsing.Text(entry, "started"), ProcessMonitorParsing.Number(entry, "cpuTicks"),
                    ProcessMonitorParsing.Number(entry, "memory")));
            }
        else complete = false;
        return new(ProcessMonitorParsing.Text(data, "boot"), ProcessMonitorParsing.Number(data, "time"), 10_000_000,
            ProcessMonitorParsing.Number(data, "cores"), complete, processes.DistinctBy(process => process.Pid).ToList());
    }

    public static MonitoringObservation Build(ProcessProbeSample sample, MonitorDefinition definition, MonitoringRecord? previous)
    {
        var count = sample.Processes.Count;
        var minimum = MonitoringOptions.Integer(definition, "minCount", 1, 0, 100000);
        var maximum = MonitoringOptions.Integer(definition, "maxCount", 100000, 0, 100000);
        bool? mismatch = count > maximum ? true : sample.Complete ? count < minimum : null;
        var quality = sample.Complete ? MonitorDataQuality.Success : MonitorDataQuality.Partial;
        var observation = new MonitoringObservation
        {
            Snapshot = new()
            {
                Quality = quality,
                Message = sample.Complete
                ? $"Matched {count} process(es); expected {minimum}–{maximum}."
                : $"Matched at least {count} process(es); process visibility is incomplete.",
                Metrics = [
                    new() { Key = "process.count", Name = "Matched process instances", NumericValue = count, Unit = "count", Quality = quality, Source = "native process metadata" },
                    ProcessMonitorParsing.Metric("process.state", "Process state", sample.Complete ? count > 0 ? "Running" : "Stopped" : "Unknown", "native process metadata"),
                    ProcessMonitorParsing.Metric("process.visibilityComplete", "Complete process visibility", sample.Complete ? 1d : 0d, "boolean", "native process metadata")
                ]
            },
            Conditions = [new("process.expected-count", "Process count differs from expected range", mismatch, ProcessMonitorParsing.Severity(definition))]
        };
        observation.State["process.boot"] = sample.Boot;
        if (sample.Time is { } time) observation.State["process.time"] = ProcessMonitorParsing.State(time);
        var previousTime = ProcessMonitorParsing.Previous(previous, "process.time");
        var sameBoot = sample.Boot.Length > 0 && previous?.State.GetValueOrDefault("process.boot") == sample.Boot;
        var countersComplete = sample.Complete && sample.Time > previousTime && sample.Frequency > 0 && sample.Cores > 0 && sameBoot;
        double cpuTicks = 0;
        foreach (var process in sample.Processes)
        {
            var identity = process.Pid + ":" + process.Started;
            if (process.CpuTicks is { } counter && process.Started.Length > 0)
                observation.State["process.cpu:" + identity] = ProcessMonitorParsing.State(counter);
            var old = ProcessMonitorParsing.Previous(previous, "process.cpu:" + identity);
            if (process.CpuTicks is null || old is null || process.CpuTicks < old || process.Started.Length == 0) countersComplete = false;
            else cpuTicks += process.CpuTicks.Value - old.Value;
        }
        if (previous is not null && previous.State.Keys.Count(key => key.StartsWith("process.cpu:", StringComparison.Ordinal)) != sample.Processes.Count)
            countersComplete = false;
        if (MonitoringOptions.Boolean(definition, "resources"))
        {
            double? cpu = countersComplete ? Math.Clamp(cpuTicks / sample.Frequency!.Value / (sample.Time!.Value - previousTime!.Value) / sample.Cores!.Value * 100, 0, 100) : null;
            double? memory = sample.Complete && sample.Processes.All(process => process.MemoryBytes is not null)
                ? sample.Processes.Sum(process => process.MemoryBytes!.Value) : null;
            observation.Snapshot.Metrics.Add(ProcessMonitorParsing.Metric("process.cpuPercent", "CPU share of host capacity", cpu, "%", "counter differences", MonitorDataQuality.Baseline));
            observation.Snapshot.Metrics.Add(ProcessMonitorParsing.Metric("process.memoryBytes", "Total resident memory", memory, "bytes", "native process counters"));
        }
        return observation;
    }

    private static string LinuxCommand(MonitorDefinition definition) => MonitoringOptions.PosixScript($$"""
        [ -d /proc/self ] || exit 127
        name={{RemoteShell.Quote(MonitoringOptions.Get(definition, "name"))}}
        path={{RemoteShell.Quote(MonitoringOptions.Get(definition, "path"))}}
        owner={{RemoteShell.Quote(MonitoringOptions.Get(definition, "user"))}}
        wanted_uid=''
        if [ -n "$owner" ]; then
          case "$owner" in *[!0-9]*) wanted_uid=$(id -u "$owner" 2>/dev/null) || { printf 'Unknown process owner\n' >&2; exit 1; } ;; *) wanted_uid=$owner ;; esac
        fi
        boot=$(cat /proc/sys/kernel/random/boot_id 2>/dev/null || true)
        sample_time=$(awk '{print $1}' /proc/uptime)
        hz=$(getconf CLK_TCK)
        cores=$(getconf _NPROCESSORS_ONLN)
        complete=1
        if [ "$(id -u)" != 0 ] && awk '$2=="/proc" && $4 ~ /hidepid=([1-9]|noaccess|invisible|ptraceable)/ {found=1} END{exit !found}' /proc/mounts; then complete=0; fi
        rows=''
        prefix=$(printf '%s' "$name" | cut -c 1-15)
        for directory in /proc/[0-9]*; do
          if ! comm=$(cat "$directory/comm" 2>/dev/null); then [ ! -d "$directory" ] || complete=0; continue; fi
          [ "$comm" = "$name" ] || [ "$comm" = "$prefix" ] || continue
          if [ -n "$path" ] || [ "$name" != "$prefix" ]; then
            if ! executable=$(readlink "$directory/exe" 2>/dev/null); then [ ! -d "$directory" ] || complete=0; continue; fi
            [ -z "$path" ] || [ "$executable" = "$path" ] || continue
            [ "$name" = "$prefix" ] || [ "${executable##*/}" = "$name" ] || continue
          fi
          if [ -n "$wanted_uid" ]; then
            uid=$(awk '/^Uid:/ {print $2}' "$directory/status" 2>/dev/null) || { [ ! -d "$directory" ] || complete=0; continue; }
            [ "$uid" = "$wanted_uid" ] || continue
          fi
          if ! stat=$(cat "$directory/stat" 2>/dev/null); then [ ! -d "$directory" ] || complete=0; continue; fi
          rest=${stat##*) }
          set -- $rest
          [ "$#" -ge 22 ] || { complete=0; continue; }
          pid=${directory##*/}
          ticks=$((${12}+${13}))
          started=${20}
          rss=$(awk '/^VmRSS:/ {printf "%.0f", $2 * 1024}' "$directory/status" 2>/dev/null || true)
          if [ -z "$rss" ]; then rss=unknown; fi
          if [ ! -d "$directory" ]; then continue; fi
          rows="${rows}$(printf 'PROC\t%s\t%s\t%s\t%s' "$pid" "$started" "$ticks" "$rss")
        "
        done
        printf 'META\t%s\t%s\t%s\t%s\t%s\n' "$boot" "$sample_time" "$hz" "$cores" "$complete"
        printf '%s' "$rows"
        """);

    private static string WindowsCommand(MonitorDefinition definition) => MonitoringOptions.PowerShellScript($$"""
        $name={{ProcessMonitorParsing.PowerShellQuote(MonitoringOptions.Get(definition, "name"))}};
        $path={{ProcessMonitorParsing.PowerShellQuote(MonitoringOptions.Get(definition, "path"))}};
        $owner={{ProcessMonitorParsing.PowerShellQuote(MonitoringOptions.Get(definition, "user"))}};
        $complete=$true;$items=@();$boot=$null;
        try { $boot=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o') } catch {};
        foreach($p in (Get-CimInstance Win32_Process | Where-Object { $_.Name -ieq $name })) {
          if($path) { if(!$p.ExecutablePath){$complete=$false;continue};if($p.ExecutablePath -ine $path){continue} };
          if($owner) { try { $o=Invoke-CimMethod -InputObject $p -MethodName GetOwner;if($o.ReturnValue -ne 0){$complete=$false;continue};if($o.User -ine $owner -and ($o.Domain+'\'+$o.User) -ine $owner){continue} } catch {$complete=$false;continue} };
          $items+=@{pid=$p.ProcessId;started=$(if($p.CreationDate){$p.CreationDate.ToUniversalTime().ToString('o')}else{$null});cpuTicks=$(if($null -ne $p.KernelModeTime -and $null -ne $p.UserModeTime){[double]$p.KernelModeTime+[double]$p.UserModeTime}else{$null});memory=$p.WorkingSetSize};
        };
        @{boot=$boot;time=([Diagnostics.Stopwatch]::GetTimestamp()/[double][Diagnostics.Stopwatch]::Frequency);cores=[Environment]::ProcessorCount;complete=$complete;processes=@($items)} | ConvertTo-Json -Depth 5 -Compress
        """);
}
