# Target views and batch operations

Named target views select current servers across static groups. Expressions support
case-insensitive tags and `NOT`, `AND`, `OR` in that precedence order. Parentheses
override precedence. Quote tags containing spaces or operator names:

```text
prod AND (web OR database) AND NOT maintenance
"AND" OR "multi word"
```

Use **Target views** to create, edit, delete and select a view. Its current aliases,
count and expression are shown before actions. Membership resolves again after each
action and immediately before a command, macro, transfer or connection plan is made.
An empty view reports that no operation was started.

```sh
ssk views create "Production web" --expression "prod AND web AND NOT maintenance"
ssk views list
ssk views edit "Production web" --expression "prod AND (web OR api) AND NOT maintenance"
ssk views preview "Production web"
ssk views select "Production web"
ssk views connect "Production web" --execute
ssk views delete "Production web" --force
```

**Batch operations** supports commands, saved macros, uploads and downloads. Each
plan copies its target configuration, macro commands and transfer destinations.
Editing hosts or tags while a batch is running does not change that plan. Duplicate
host IDs execute once. Downloads reserve separate filenames for each host and avoid
existing local files. Local source file contents themselves are not snapshotted.

The TUI previews operation, target aliases/count, concurrency and paths before asking
to execute. `Ctrl+C` cancels active operations and prevents workers from starting
additional targets. Reports retain success, failure, cancellation and not-started
outcomes separately, including duration and exit code when available. One host's
failure does not stop other hosts. Transfers use the noninteractive SSH transport:
unknown host keys and missing credentials fail instead of opening competing prompts.

CLI operations preview by default. Explicit `--execute` starts them. Select exactly
one server, static group, tag or named view:

```sh
ssk batch command "uptime" --view "Production web"
ssk batch command "uptime" --view "Production web" --concurrency 4 --execute
ssk batch macro "System check" --group production --execute --output json --report result.json
ssk batch upload --local ./settings.conf --remote /tmp/settings.conf --tag web --execute
ssk batch download --remote /var/log/example.log --local ./downloads --view "Production web" --execute --output csv
ssk views command "Production web" "uptime" --execute
ssk views macro "Production web" "System check" --execute
```

Concurrency accepts 1–32 workers, with commands/macros defaulting to four and transfers
to one. `--output table|json|csv` chooses the final report representation. JSON/CSV
stdout contains just the report when execution is requested; `--report FILE` saves a
JSON copy for later selection of failed targets. Any unsuccessful target produces
exit code 1, or 130 when cancelled. Reports exclude command text, command output,
transfer paths and credentials; errors are limited to safe exception categories or
generic nonzero-exit messages. Existing audit privacy, per-host credential and
connection/recording policies apply in the underlying operation services.

Table-mode CLI execution and TUI commands/macros also show command output separately,
with a host prefix on each line. Parallel callbacks write one host chunk at a time,
and terminal escape/control sequences are removed. Raw output is never added to the
JSON/CSV report; JSON/CSV CLI modes keep stdout machine-readable.

Retries are never automatic. In the TUI, explicitly enter failed host IDs, inspect
the new preview and confirm. This reuses the original in-memory host and operation
snapshots. Cancelled, unstarted and successful targets cannot be selected for this
failed-target retry.

For a later CLI retry, provide the operation again, its earlier JSON report and the
specific failed IDs. The retry takes a **new snapshot of current host configuration**
for those IDs; removed targets reject the entire plan. Its operation kind must match
the report. Inspect the preview before adding `--execute`. Because exported reports
exclude executable text and secrets, they never silently replay an old command.

```sh
ssk batch command "uptime" --retry-report result.json --failed HOST_ID_1,HOST_ID_2
ssk batch command "uptime" --retry-report result.json --failed HOST_ID_1,HOST_ID_2 --execute
```
