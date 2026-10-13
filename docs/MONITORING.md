# Monitoring

Open **Monitoring** from the terminal menu to create, edit, test or delete definitions, inspect snapshots and alarm history, and acknowledge alarms. Each definition selects one saved host and a provider. Saving a definition does not connect to a host. **Check now** runs one collection; **Watch** runs enabled definitions until Ctrl+C. Continuous monitoring is off by default and must be enabled explicitly. It ends when the process exits.

```sh
ssk monitor status --server web --output json
ssk monitor run --group production
ssk monitor watch --tag production --kind service
ssk monitor enable
ssk monitor disable
ssk monitor enable cpu-web
ssk monitor create --file cpu-web.json
ssk monitor ack cpu-web --alarm high-memory
ssk monitor delete cpu-web
```

Use one of `--server`, `--group` or `--tag`, or omit the selector for all configured hosts. `--kind` accepts `server`, `process`, `service`, `log` or `container`. `status` reads local cached results without SSH; `run` collects once; `watch` publishes repeated snapshots. JSON uses source-generated serialization and contains no ANSI presentation. Exit codes: 0 complete OK, 1 warning/critical, 2 missing/incomplete/failed data or invalid arguments, 130 cancellation. An empty result is incomplete.

Example `cpu-web.json` (replace `HOST_ID` with the saved host ID):

```json
{
  "id": "cpu-web",
  "name": "CPU and memory for web",
  "hostId": "HOST_ID",
  "provider": "server",
  "intervalSeconds": 60,
  "timeoutSeconds": 15,
  "freshnessSeconds": 180,
  "options": { "platform": "linux" },
  "rules": [
    {
      "id": "high-memory",
      "name": "Memory pressure",
      "metricKey": "memory.percent",
      "comparison": "GreaterOrEqual",
      "threshold": 90,
      "clearThreshold": 85,
      "severity": "Warning",
      "consecutiveSamples": 2,
      "holdSeconds": 30
    }
  ]
}
```

The numeric rule editor uses metric keys from a probe snapshot. It supports six comparisons, warning/critical severity, consecutive fresh samples, hold time and an optional separate clear threshold. Provider conditions describe states such as hardware failure, missing containers or incomplete coverage. Failed/unknown samples interrupt a pending consecutive sequence and preserve an existing active alarm; they cannot produce recovery. Acknowledgement affects notification state, not the measurement. Alarm transitions are deduplicated and history is bounded.

Definitions live in `appconfig.json`. Snapshots, counters, log cursors and alarms live separately in the private `monitoring` directory beside it. Atomic updates and per-monitor leases coordinate multiple processes. Collections on a host are serialized within a process; at most four hosts run concurrently (configurable downwards). Each probe has a timeout and a combined stdout/stderr byte limit enforced while reading. Failures back off subsequent polling. Configuration or endpoint changes invalidate cached results and reset baselines; changes during a probe cannot publish success for the new target.

Cached values show their sample time and data quality. Unchecked, baseline, partial, authentication-required, permission-denied, unsupported, timeout, error, stale and disabled values remain distinguishable. Missing fresh metrics can retain a clearly stale previous value. A stale or incomplete result cannot claim complete OK. CPU counter deltas need an initial baseline and reset after reboot or decreasing counters.

| Provider | Capabilities and limits |
| --- | --- |
| `server` | Linux CPU from `/proc/stat` deltas, memory from `MemAvailable`, swap, `df -Pk` filesystem capacity and uptime. Windows CPU/memory/logical disks/uptime from explicit noninteractive PowerShell/CIM. Automatic platform detection or explicit Linux/Windows selection. |
| `smart` | Linux `smartctl -a -j` for a configured device, including valid warning exit bits, temperature and supported ATA/NVMe values. Windows Storage/Reliability cmdlets when available. Missing tools, permissions or hardware data remain unknown. No self-tests are started. |
| `updates` | Ubuntu/Debian `apt-get --simulate upgrade` using the existing index, index age and reboot-required marker. Optional Windows Update Agent search and reboot hint. No index refresh, installation or reboot. Unsupported update classifications remain unavailable. |
| `service`, `process` | [Process/service monitoring](PROCESS_MONITORING.md): systemd/process identities and resources, restart evidence; Windows capability limits. |
| `log` | [Log monitoring](MONITORING_LOGS.md): Linux regular text files, literal/regex patterns, observation-time windows and conservative coverage. |
| `container` | [Docker/Compose monitoring](MONITORING_CONTAINERS.md): fresh identity, state, health, resources and observed restart deltas. |

Percentages and byte/count units are included in each metric. Linux memory usage uses available memory, not merely free memory. The CPU sum excludes guest counters already included in user/nice time. Windows uses the formatted aggregate CPU counter; missing counters produce unavailable values. Filesystem metrics follow the `df` projection, and Windows logical drive values follow CIM. SMART capability varies by hardware. Update counts mean candidates according to the index/search source, not a guarantee that a system is current. Prefer hourly SMART polls and six-hour update polls; Windows update searches need a longer timeout.

Probes reuse the existing credential/tunnel lease and strict SSH host-key verification. Configure and trust a host through the regular connection flow first. Watch never opens authentication or trust prompts. Native Bastion AAD routes without a reliable SSH exit contract are unsupported; supported key/password SSH tunnels retain their normal lease cleanup. Probes do not run hooks or add recent connections or session recordings. Audit stores quality/alarm transitions and definition metadata, without raw probe output.

Read as the configured SSH user by default. `allowSudo` is an explicit provider option for privileged probes: use configured protected stdin credentials or `sudo -n`; missing permission yields an error. Nothing installs tools or changes groups/sudoers automatically. Do not put passwords in definitions or custom SSH options. Remote values and labels are rendered as literal text with terminal control sequences removed.

Validation covers parser fixtures, simulated clocks, transport cleanup, alarm sequences, endpoint changes and actual bounded child processes. Linux CI exercises file reads and rotation; Windows tests exercise PowerShell syntax and child-process cleanup. Real Ubuntu/Windows SSH, SMART hardware, WUA and Docker daemon smoke tests require configured remote infrastructure and have not been performed as part of this change. No remote upgrade, restart or container mutation is needed for the automated tests.
