# Docker containers

Use **Docker Containers** in the main menu to scan one or several configured servers, select containers, and operate on a batch. Server actions also provide an individual Docker scan; container actions expose interactive `Exec`, streaming logs and Docker operations.

## Authentication and discovery

SSH username and authentication come from the existing server profile. Password authentication supports a configured secret reference, the Windows DPAPI credential store, or a password entered for the current action. A prompted password is kept in memory and is never written to `appconfig.json`.

Docker commands first use the remote user's Docker endpoint when no sudo credential is configured. A daemon socket permission error can fall back to noninteractive `sudo -n`. If a sudo password or secret reference is configured, commands use `sudo -S` with the password passed through protected SSH standard input. Configure credentials in the server profile when authentication fails. SSH/sudo credentials are never passed in process arguments. This SSH-based workflow does not expose a Docker TCP daemon or implement Docker daemon HTTP Basic authentication.

Scans use full container IDs, machine-readable JSON, and Compose project labels. Docker Compose discovery is optional; an unavailable Compose plugin produces a warning while Docker container discovery remains usable. SSH, authorization, missing Docker, daemon failures and invalid JSON produce actionable scan errors. Standard error is not mixed into JSON input.

## Operations and cancellation

Available operations are Start, Stop, Restart, Pause, Unpause and Remove. Multi-selection supports containers on several servers. Batch scans and operations run at most four targets concurrently by default; the service API accepts limits between one and sixteen. Every target has its own outcome, including failures and cancellation. A command/scan has a 45-second deadline. Ctrl+C cancels the current UI batch and terminates local SSH work; an already transmitted remote operation may have completed, so rescan before retrying.

Operations use a stored Docker ID when one exists, preventing an old saved container from silently targeting a replacement with the same name. A successful operation invalidates the scan cache timestamp. Remove requires explicit confirmation, uses `docker rm` without force or volume deletion, and retains the saved local alias/group/tag entry. Stop a running container before removing it.

Interactive Exec and Logs still hand the terminal to SSH and sudo for native prompts. They safely quote the container name and never resolve a sudo secret into command text.

Captured scans, operations and mixed-target commands use noninteractive SSH with strict host-key checking, disabled TTY/local commands/forwardings, and a combined 16 MiB stdout/stderr limit. Unknown host keys must first be verified through the normal SSH workflow. The limit also applies when supplying sudo input; exceeding it terminates local SSH capture and reports a failed target rather than a successful empty result.

## Groups, tags and commands

Save a discovered container to assign an alias, group and tags. Edit the saved entry to change those fields. Docker Containers provides group, tag and free multi-selection. Tags select both servers and saved containers, with each target matched against its own tags; a container does not inherit its parent server's tags. Multiple selected tags use an intersection.

The tag menu can run a command on all matching servers and containers. The `IContainerGroupService.RunOnTargets` API also accepts explicit mixed `ConnectionTarget` objects. Server targets run the command on the SSH host. Container targets run the entire command through `docker exec ... sh -lc`, so shell operators and variable expansion stay inside the container. Containers need a compatible `sh`; images without one report a per-target failure. Duplicate host/container targets are executed once.

Scanned state is a snapshot, not continuous monitoring. Use the Monitoring module for periodic checks and alarms.
