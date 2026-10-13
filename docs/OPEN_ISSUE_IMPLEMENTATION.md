# Open issue implementation

This change implements the 15 open issues selected from `ms-sk/sshocked`, excluding #77 (GUI). The default entry point remains the terminal application.

| Issue | Delivered behavior | Guide |
| --- | --- | --- |
| #58 | Saved AND/OR/NOT target views with current membership previews and shared operations | [Fleet operations](FLEET_OPERATIONS.md) |
| #59 | Local/remote/dynamic forwarding profiles, validated binds, readiness, owned process and credential cleanup | [Port forwarding](PORT_FORWARDING.md) |
| #60 | Immutable confirmed batch plans, bounded concurrency, cancellation, per-target reports and explicit failed-target retries | [Fleet operations](FLEET_OPERATIONS.md) |
| #61 | Ctrl+P palette across actions/hosts/groups/tags, F1 assigned-shortcut help and restored selection after Esc | [Display and navigation](VISUALS.md) |
| #62 | Versioned portable export, conflict/change preview, atomic merge/replace and exact private local recovery | [Configuration backup](CONFIGURATION_BACKUP.md) |
| #63 | Previewable redacted local diagnostics with tools, route status, privacy-gated details and no automatic upload | [Diagnostics](DIAGNOSTICS.md) |
| #67 | One dark palette; legacy Light settings load with Dark; theme selection removed | [Display settings](VISUALS.md) |
| #68 | Password/DPAPI/secret-authenticated SSH Docker access and protected sudo input with clear authentication failures | [Containers](CONTAINERS.md) |
| #69 | Start/stop/restart/pause/unpause/confirmed remove, multiselect scans/actions, container groups/tags and mixed commands | [Containers](CONTAINERS.md) |
| #70 | Shared batch execution, bounded SSH policy/output reader, dedicated Docker/parser/monitoring helpers, shared transfer validation and removal of the light theme branch | Feature guides and regression suite |
| #72 | Monitoring core, bounded transport, cache freshness/scope, scheduler, quality states, alarm history; Linux/Windows server, SMART and update providers | [Monitoring](MONITORING.md) |
| #73 | Read-only process/service providers with identities, resources, systemd states and evidenced restart/reload/reboot hints | [Processes and services](PROCESS_MONITORING.md) |
| #74 | Linux log-file cursor/fingerprints, complete lines, pattern preview, sliding count windows and conservative coverage | [Logs](MONITORING_LOGS.md) |
| #75 | Docker/Compose identity, replica availability, expected states/jobs, health/OOM/resources and observed restart baselines | [Container monitoring](MONITORING_CONTAINERS.md) |
| #76 | Independent macro drafts, create/edit/duplicate/reorder, templates, conflict validation and existing execution paths | [Macros](MACROS.md) |

Configuration changes use the existing repository update lock and source-generated JSON. Runtime monitoring and forwarding state is separate from configuration. Existing hooks, recording, credential leases and auditing remain the underlying policies; probes do not run connection hooks or write user connection history.

Release tests include real bounded child processes, a shell whose background child keeps its pipes open, forwarding lifecycle, SSH argument inspection, configuration recovery and parser fixtures. The CI additionally publishes the existing `linux-x64`, `linux-arm64`, `win-x64` and `osx-x64` Native AOT release targets and starts native help where executable on its runner.

Remote Ubuntu/Windows SSH, SMART hardware, Windows Update Agent and live Docker integration require configured infrastructure and remain separate smoke tests. See each guide for supported capabilities and unavailable values. The automated suite performs no remote package upgrades, service restarts or Docker mutations.
