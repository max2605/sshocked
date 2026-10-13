sshocked 2.2.0 adds monitoring and fleet operations to the terminal application, implementing fifteen issues from [PR #78](https://github.com/ms-sk/sshocked/pull/78): #58–#63, #67–#70 and #72–#76.

## Highlights

- **Monitoring:** configure server health/load/updates, processes, services, Linux log files and Docker/Compose checks. Inspect cached snapshots, run a check or start a watch with explicit intervals, output limits, quality states and delayed alarm rules. Stale or missing data cannot claim complete OK; acknowledgement does not change the measured state.
- **Fleet views and batches:** save AND/OR/NOT tag expressions and preview current membership. Run confirmed command, macro and transfer plans with bounded concurrency, independent target outcomes, JSON/CSV reports and explicit failed-target retries. Raw command output is displayed separately and excluded from reports.
- **Docker management:** scan password/DPAPI/secret-authenticated SSH hosts, select multiple containers, start/stop/restart/pause/unpause or confirm removal. Containers can join groups and tags alongside servers. Privileged access uses protected stdin; scans and captured commands use bounded noninteractive SSH execution.
- **SSH forwarding:** create local, remote and dynamic forwarding profiles with loopback defaults, startup/readiness checks and owned process/credential lifetimes. Start/stop them in the TUI or CLI.
- **Macro editor:** create, edit, duplicate and reorder commands without recording or connecting. Independent drafts support save/discard and conflict validation. The Ubuntu/Debian update template remains editable and runs only after an explicit execution action.
- **Backup and diagnostics:** export portable redacted configuration, inspect restore changes/conflicts and apply merge/replace atomically with an exact private local recovery snapshot. Preview and manually save redacted connection diagnostics; nothing is uploaded automatically.
- **Navigation:** Ctrl+P searches current actions, hosts, groups and tags; F1 shows assigned shortcuts. Dark is now the only palette; legacy Light settings load with Dark.

## Monitoring and CLI

```sh
ssk monitor status --server web --output json
ssk monitor run --group production
ssk monitor enable
ssk monitor watch --tag production --kind service
ssk views create "Production web" --expression "prod AND web AND NOT maintenance"
ssk batch command "uptime" --view "Production web"
ssk batch command "uptime" --view "Production web" --execute --output json
ssk forward create database --server prod-db --kind local --listen-port 15432 --target 127.0.0.1 --target-port 5432
ssk forward start database
```

Define monitors in **Monitoring** or use `ssk monitor create --file definition.json`. Continuous monitoring is disabled by default, runs only during an explicitly started watch, and stops with the application. Monitoring definitions and runtime snapshots/history are stored separately. Probes use strict trusted host keys, configured credential/tunnel leases and bounded output; they do not run connection hooks or create recent sessions/recordings. Monitoring never installs updates, reboots a host or repairs a service/container automatically.

Linux/Ubuntu server probes use CPU deltas, available memory, filesystem capacity and uptime. Optional SMART depends on installed tools, hardware support and rights. Ubuntu/Debian update counts use the existing package index and expose its age; no index refresh or upgrade runs. Windows over OpenSSH uses explicit noninteractive PowerShell/CIM and capability-based Storage/Windows Update Agent checks. Missing capabilities remain unavailable rather than becoming zero/healthy values; a universal per-service Windows restart-needed value cannot be inferred.

Log monitoring currently supports regular UTF-8/LF Linux files with Bash/GNU utilities, literal/regex patterns and observation-time count windows. Rotation, restart, backlog and read failures retain conservative coverage. Historical log timestamps, journald, multiline and Windows log/EventLog providers remain future extensions. Container monitoring supports Docker Engine/Compose on Linux, including rootless direct access; Podman, Kubernetes, LXC and Windows Docker Engine providers remain future extensions.

## Compatibility and platform notes

Existing configuration and credential references remain readable. GUI work in [#77](https://github.com/ms-sk/sshocked/issues/77) is excluded; launching `ssk` without arguments still opens the terminal application. The removed Light setting is migrated to the dark palette.

Portable backups exclude credential values and local password ciphertext; password files and private keys are not copied. Local passwords must be re-saved for another account or endpoint. Exact recovery snapshots are private, tied to the current account/computer and handled separately from portable imports. Diagnostic paths and command text require explicit inclusion and compatible audit privacy settings.

Background probes, batches and managed forwards do not open authentication or host-key prompts. Trust/configure hosts through the normal connection flow first. Native Bastion AAD routes without a checked SSH exit contract remain unsupported for these operations; use supported key/password tunnels. Macro sessions still require POSIX `sh`, and interactive programs should use Connect. The package-upgrade template requires suitable noninteractive sudo permissions and does not guarantee that every package script is noninteractive.

Native AOT downloads remain available for **Windows x64, Linux x64/ARM64 and macOS x64**, with Debian packages for both Linux architectures. OpenSSH Client is required. Windows ARM64 and macOS ARM64 archives are not supplied by this workflow.

## Validation

The merged implementation passed 583 tests on Linux, including real log descriptor/rotation/read-failure tests. Windows passed 581 tests with two platform-dependent symbolic-link tests skipped. Native AOT publishing passed for all four existing release targets, with native CLI checks on Windows x64, Linux x64 and macOS x64; build output reported zero warnings. Tests cover bounded child output, cancellation/cleanup, forwarding lifecycle, private recovery, alarms and changed monitor endpoints. Session lifecycle detects shell exit even when a background child holds output pipes open.

Live Ubuntu/Windows SSH, SMART hardware, Windows Update Agent and Docker daemon smoke tests require configured infrastructure and were not performed. The automated suite performs no remote upgrades, restarts or container mutations.

See the [implementation map](https://github.com/ms-sk/sshocked/blob/v2.2.0/docs/OPEN_ISSUE_IMPLEMENTATION.md), [monitoring guide](https://github.com/ms-sk/sshocked/blob/v2.2.0/docs/MONITORING.md), [fleet guide](https://github.com/ms-sk/sshocked/blob/v2.2.0/docs/FLEET_OPERATIONS.md), [macro guide](https://github.com/ms-sk/sshocked/blob/v2.2.0/docs/MACROS.md), [backup guide](https://github.com/ms-sk/sshocked/blob/v2.2.0/docs/CONFIGURATION_BACKUP.md) and [forwarding guide](https://github.com/ms-sk/sshocked/blob/v2.2.0/docs/PORT_FORWARDING.md).

**Full changelog:** https://github.com/ms-sk/sshocked/compare/v2.1.1...v2.2.0
