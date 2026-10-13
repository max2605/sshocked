# Process and service monitoring

The `process` and `service` providers use the common monitoring scheduler, bounded
SSH transport, runtime store and alarm evaluator. Add definitions in **Monitoring**
or with `ssk monitor create --file definition.json` and select `--kind process|service` when running, watching
or displaying results. No probe starts, stops, restarts or kills a process/service.

Set `platform=linux|windows` explicitly for the target OS. A `service` definition
selects an exact systemd unit name (for example `nginx.service`) or Windows service
`Name`, with `expected=running|stopped`. Systemd `active/exited` is a successfully
active oneshot unit. Missing, masked, failed and transition states remain distinct.
Windows automatic start mode is displayed separately from actual running state.

A `process` definition selects an exact process name, optional executable `path`
and optional `user`, plus `minCount` and `maxCount`. Set both counts to zero to expect
no running instances. Linux uses `/proc` metadata rather than formatted `ps` output;
short names match `comm`, and longer executable names also verify `/proc/PID/exe`.
Windows names include their `.exe` suffix and use local-on-target CIM via SSH.
User names, domain-qualified Windows owners and Linux numeric UIDs are supported.
No command lines or environment variables are collected.

If `/proc` access, `hidepid`, executable-path permissions or Windows ownership
queries prevent complete matching, the count is a lower bound. Incomplete visibility
never proves that a required process is missing and never recovers an existing
absence alarm. A known observed count above the maximum can still raise an alarm.
Processes that vanish during sampling are skipped. Permission and transport errors
retain their own monitoring quality states.

`resources=true` adds memory in bytes and CPU percentage. CPU means share of total
host CPU capacity, normalized by logical processor count, using native counter
differences. Linux samples identify processes using PID, start ticks and boot ID;
Windows uses PID, creation timestamp and boot timestamp. Missing baseline, identity
change, counter reset, host restart and changing process membership yield an unknown
CPU sample, rather than 0%. Missing resource permissions produce nullable metrics.

`severity=warning|critical` controls the built-in state/count alarm. The definition's
grace period and consecutive-sample settings apply through the shared evaluator.
Custom numeric rules may target `process.count`, `process.cpuPercent`,
`process.memoryBytes`, `service.cpuPercent`, `service.memoryBytes`, or other displayed
numeric metrics.

Restart requirement is separate from running state. Linux systemd `Markers` can
provide `needs-restart`; absence of a supported marker field is `Unknown`.
`NeedDaemonReload` is a separate configuration-reload hint. An explicitly enabled
`needrestart=true` executes only installed `needrestart -b -r l`, matches exact unit
names and never restarts anything. Missing tools or non-root visibility give an
unknown restart requirement. Configure `allowSudo=true` only when needed and when
the existing sudo credential/noninteractive permission setup supports it.

Windows services report restart requirement as `Unknown`, because there is no
universal reliable per-service restart indicator. Stopped state, high CPU and
available package updates are never interpreted as restart evidence. Host reboot
requirements belong to the separate server/update providers.

For optional deployment smoke tests, configure a known Ubuntu systemd service and
two harmless process instances, then compare one-shot results to native metadata.
On Windows with OpenSSH, configure one stopped and one running service and verify
that local CIM results arrive through the explicit PowerShell launcher. Production
checks need suitable credentials and trusted SSH host keys. Automated tests use
fixtures and never change real remote hosts.
