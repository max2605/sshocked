# Session recording and replay

Session recording is disabled by default. Enable it with `ssk sessions enable`,
disable the global default with `ssk sessions disable`, and inspect settings with
`ssk sessions status`. A host's nullable `recordSession` setting overrides the global
default; explicit entries in `excludeHosts` always opt that host out. When configured,
`includeHosts` selects specific host IDs or aliases. Host matching is case insensitive.

## Supported recording backend

The first backend captures stdout and stderr through pipes and forwards both streams
to the terminal. It records Unicode text chunks and their elapsed times in asciinema
cast version 2. Input stays attached to the existing terminal and is never intercepted
or stored as a separate input stream. Text echoed by the remote server, including
typed commands, is output and can therefore appear in a recording.

This backend provides output-only capture. It does not supply a PTY, capture terminal
size changes, or reproduce all interactive terminal behavior. Programs can change
their buffering, colors or TTY behavior when output is redirected. FullTerminal,
ScriptFormat, and input capture settings fail with explicit unsupported-backend
messages. Use key or agent authentication, or the configured SSH askpass secret
integration; this backend does not provide a password-input transport.

Azure Bastion recording uses tunnel mode and normal SSH authentication. Native Azure
CLI Bastion sessions cannot be recorded by this backend. The application prints the
recording ID before capture, and completed or interrupted sessions have an associated
metadata file containing their outcome. Output can contain passwords, tokens and other
sensitive content; recordings are not automatically redacted or encrypted.

## Storage and retention

Recordings live in `%APPDATA%\sshocked\sessions` on Windows and
`~/.config/sshocked/sessions` on Unix, under UTC `YYYY/MM/DD` directories. Cast files
have a matching JSON metadata file containing host identity, start/end time, duration,
size, exit code, and `output-only` recording type. No process environment variables are
copied into metadata. Generated directories and files use owner-only permissions on
Unix; Windows uses the user's existing profile-directory permissions.

Completed recordings are gzip-compressed by default. Replay and export automatically
decompress them. Retention defaults to 30 days and 1000 MiB; old completed recordings
are removed first. Cleanup runs before and after a recording and when listing sessions.
Active recordings are preserved, so the size setting is a cleanup target rather than
a live hard disk quota. A retention value of `0` disables age cleanup; a null size
limit disables size cleanup.

Recording paths must stay within the sessions directory. IDs accept an unambiguous
hexadecimal prefix of at least six characters. Malformed metadata files are ignored
when listing; invalid event streams are rejected during playback.

## Commands

```text
ssk sessions list --last 10 --server web-prod-01 --since 2026-10-01
ssk sessions replay <session-id> --speed 2
ssk sessions replay <session-id> --no-controls
ssk sessions export <session-id> --output session.cast
ssk sessions export <session-id> --format text --output session.txt
ssk sessions delete <session-id>
ssk sessions delete --older 30d
```

Replay speed ranges from 0.1 to 16. On an interactive terminal, Space pauses/resumes,
Left/Right seek ten seconds, Up/Down change speed, and Q quits. Seeking backward clears
the screen and reconstructs output up to the selected time. Redirected playback uses
timing and speed without keyboard controls. Interactive seeking loads the event
timeline into memory; large sessions require correspondingly more memory.

Cast export produces a standalone asciinema-compatible file. Text export concatenates
the captured output, including any terminal escape sequences. Export does not render
video or upload content. Existing output files are preserved; choose a new output path.

## JSON settings

Merge this root property into the existing configuration. Enum mode `1` means
OutputOnly and mode `4` means AsciinemaCast; both use the output-only backend.

```json
{
  "sessionRecording": {
    "enabled": false,
    "mode": 1,
    "captureInput": false,
    "captureOutput": true,
    "captureTiming": true,
    "retentionDays": 30,
    "maxTotalSizeMb": 1000,
    "compress": true,
    "includeHosts": {},
    "excludeHosts": {}
  }
}
```
