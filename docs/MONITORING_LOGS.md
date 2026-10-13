# Remote log monitoring

The `log` provider reads ordinary LF-delimited UTF-8 files on a Linux SSH host. Add a monitor in Monitoring with an absolute `path`, line selection (`all`, `literal` or `regex`), explicit case handling, and a sliding window. Configure count rules against `log.lines` or `log.matches`; `> 10` triggers only at 11, while a pattern alarm can use `>= 1`. `LogMonitoringProvider.TestPattern` evaluates up to 1,000 local example lines without SSH.

Use `ssk monitor status|run|watch --kind log`. The provider requires Bash, GNU-compatible `stat`, `dd`, `sha256sum`, `base64` and `/proc` file descriptors; it does not install tools or modify remote logs. Bash pipefail prevents failed reads in hash/base64 pipelines from appearing as successful empty data. Paths are safely quoted and patterns are evaluated locally. Sudo is explicit and uses the configured noninteractive credential policy.

## Time and coverage

Windows range over `(observation time - duration, observation time]`. Every complete line in a read receives the same client observation time. Replayed backlog and `fromBeginning` input are counted at read time, regardless of timestamps printed inside the log. A day is 24 hours; windows are limited to 30 days. Seconds are supported, but a longer poll/read interval cannot provide exact write-time resolution and produces a visible diagnostic.

The default `fromNow` starts at EOF and ignores an already unfinished line through its next LF. `fromBeginning` reads existing content in bounded chunks. Each line counts once per pattern even if it contains several matches; identical text at different offsets counts separately. CRLF is normalized. Partial lines and UTF-8 sequences are reread from the last committed LF instead of being persisted as raw text.

Within a process, bucket aging uses monotonic elapsed time. Process restarts, wall-clock jumps, interrupted/late access, rotations, truncation, read backlog and resource-limit losses restart coverage. Incomplete counts are lower bounds: a proven excess can raise an alarm, but incomplete/stale data cannot clear one or prove absence. Coverage becomes complete only after a full uninterrupted window. Restarting the client clears old time buckets conservatively while retaining the committed byte cursor.

## Identity, rotation and limits

Every bounded read opens one regular-file descriptor and uses it for metadata, hashes and payload. Header/footer identity and prefix/checkpoint anchors detect mixed generations or in-place edits during a read. Cursor state records device/inode, epoch, byte offset, fingerprints and a skip flag. The shared monitoring store commits cursor, window counts and alarm state atomically.

Rename/create rotation starts the new inode at byte zero and marks unavailable old-generation tail data as a coverage gap. This polling MVP does not search arbitrary directories for renamed files. Copytruncate detects smaller files and changed anchors, including many fast-regrowth cases, and resets the epoch. Copytruncate has an inherent copy/truncation loss interval; identical rewrites outside sampled anchors cannot be guaranteed detectable. There is no claim of exactly-once remote collection across lost file generations.

Defaults are 32 KiB/read, 16 KiB/line, 16 MiB/backlog, 4,096 retained time buckets and 100 ms/regex match. Read size supports 1–256 KiB, line size at most 64 KiB and strictly smaller than a read to leave space for LF, backlog at most 64 MiB, and at most 10,000 buckets. Backlog overflow skips unread bytes with an explicit gap. Oversized/invalid UTF-8 lines or regex timeouts advance safely with incomplete coverage. Bucket overflow drops older counters with a gap instead of silently claiming a complete count. Long windows with short polls may require a larger bucket limit or a longer interval.

No raw lines, match text or partial bytes are written to state/audit. Persisted data contains only offsets, hashes, aggregate timestamps/counts and coverage. File absence and authorization/transport failures are explicit errors, never zero matches. EventLog, journald, multiline records, compressed archives and historical log-timestamp interpretation remain future providers.
