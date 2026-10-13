# Activity and recording

Activity and recording is the shared module for operation events and session playback.
Settings opens the module, including one timeline, recording
browsing, replay, export, deletion and both capture policies.

The overlap review found that both features describe server activity, but their
retained evidence differs. Events record operation outcomes, timestamps, targets,
exit codes and optionally redacted command text. Output recordings preserve the
terminal output and timing needed for playback. Removing either would lose one of
those capabilities, so the application now presents them together while retaining
their existing storage formats, privacy switches and independent retention limits.
Large recordings cannot evict the event history, and clearing event history does
not delete recordings.

```text
ssk activity status
ssk activity events --last 30 --server web --output json
ssk activity audit settings --command-text false --retention-days 90
ssk activity recordings list --server web
ssk activity recordings replay SESSION_ID
ssk activity recordings export SESSION_ID --format text --output "C:\My recordings\session.txt"
```

Existing `history`, `audit` and `sessions` commands remain compatibility aliases.
Existing events and casts remain readable; consolidation does not require moving
or converting files. Existing per-host recording overrides, exclusions, timing,
compression, export formats and retention rules still apply.

Output recording remains disabled by default. It records output, which can contain
sensitive data, and does not capture SSH authentication input. Managed command
sessions also produce output casts when the recording policy applies. Explicit
macro recording captures commands from the managed command prompt separately;
see [Macros](MACROS.md) and [Session recording](SESSION_RECORDING.md).
