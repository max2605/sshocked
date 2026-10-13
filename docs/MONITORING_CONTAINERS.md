# Container monitoring

The `container` provider monitors Docker Engine on a Linux SSH host. Register a monitor through Monitoring and use `ssk monitor status|run|watch --kind container`. The provider is read-only: it never starts, stops, restarts or enters a container. Podman, Swarm, Kubernetes, Windows Docker Engine and LXC are currently unsupported.

## Target and access

- `targetMode=name`: an exact `target` name, following a replacement with the same name.
- `targetMode=id`: an exact full 64-character Docker ID, retaining its original instance identity.
- `targetMode=compose`: exact `project` and `service` canonical Compose labels. `minReplicas` sets the minimum acceptable count, between 1 and 256.

Each poll uses fresh discovery and minimal inspection of selected identities. Inspections and stats use batches of at most 32 IDs; a discovery/inspection race is retried once. Credential, transport and daemon errors retain unknown/stale semantics through the monitoring core rather than reporting every container missing.

`allowSudo=false` uses the SSH user's Docker endpoint and supports rootless Docker. `allowSudo=true` uses the configured noninteractive sudo policy. There is no automatic sudo fallback or credential prompt in a poll, because sudo can select a different daemon. Existing SSH and sudo secret references and protected stdin remain in use.

## Expectations and conditions

`expected=running` requires running containers. `expected=stopped` accepts created or exited containers. `expected=job` accepts created/running jobs and exited jobs with `expectedExitCodes` (comma-separated values, default `0`). RequireHealthy is available for the running expectation and counts only an explicitly healthy Docker healthcheck. Missing checks report `notConfigured`; starting healthchecks and missing checks do not satisfy RequireHealthy. Set the common monitor grace interval for deployments/startup.

Stable aggregate conditions cover missing/unavailable targets, unexpected state, health, exit codes, OOMKilled and observed RestartCount increases. Per-replica metrics retain the inspected state and health. An OOMKilled flag reports Docker's last container state; it is not a complete incident history.

RestartCount differences require the same daemon ID, access mode and full container ID. Initial inspection, replacement, daemon change or a counter decrease establishes a new baseline. The resulting delta is an observed counter increase, not a guaranteed complete restart event stream or a timestamped event count over an offline window.

## Resource metrics

`collectStats=true` collects Docker CLI statistics for running containers. Common aggregate rule keys are `cpu.percent`, `memory.bytes`, `memory.percent`, `containers.ready` and `restart.increase`. Replica keys start with `container.<full-id>.` and include state, health, exitcode, oom, restart.total, restart.increase, cpu.percent, memory.bytes, memory.percent, memory.configured.limit, memory.reported.limit, network.rx/tx, block.read/write and pids.

CPU uses Docker's reported scale and can exceed 100%; aggregate CPU is a sum, not host capacity normalization. Linux Docker CLI memory usage excludes reclaimable cache. Memory percentages use Docker's reported effective limits; configured limits are a separate metric, and zero configured limits mean no explicit limit. Compose aggregate memory percent is the sum of usage divided by the sum of reported limits. Formatted CLI sizes introduce rounding into converted byte values and aggregates. Network/block values are cumulative counters and PIDs includes threads.

Missing, stopped or malformed stats are unavailable (`null`), never manufactured zero measurements. Partial stats retain useful inspected states. No environment variables, complete inspect objects, healthcheck command output or mount paths are stored. Numeric parsing uses invariant culture and distinguishes SI units such as MB from binary units such as MiB.
