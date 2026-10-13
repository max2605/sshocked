# Command sessions and macros

Choose **Run command** in any server context menu to open a managed command
session with live output. This is a persistent remote POSIX shell: `cd`, exported
variables and shell functions survive between commands. Shell completion markers
are private protocol data and are excluded from visible output and output casts.

```text
web> :record
web [REC]> cd /var/www
web [REC]> export APP_ENV=production
web [REC]> ./health-check.sh
web [REC]> :stop
web> :save production-health
web> :exit
```

Use `:discard` to discard a draft. Only submitted commands between `:record` and
`:stop` become macro steps. Control commands, later commands and SSH authentication
input are never recorded. A macro must be stopped and contain at least one command
before it can be saved. Names are unique without regard to letter case. Commands
that contain recognized secret options or the active SSH/sudo password are rejected
before capture and execution. Avoid arbitrary secret literals: pattern matching
cannot identify every secret. Keep credentials in remote credential references.
Saved command text is part of the local configuration.

The managed prompt is command aware and does not intercept keystrokes in a native
SSH terminal. Commands receive closed standard input so interactive programs and
password prompts cannot consume the protocol. Use **Connect** for full terminal
programs. Managed command sessions require `sh` on the remote server. Native Azure
Bastion AAD connections need an SSH key/password tunnel for this mode.

Macros can be selected from a server, group or tag menu, or managed through the
**Macros** main menu. The CLI supports the same targets:

```text
ssk macros record --server web
ssk macros list
ssk macros run production-health --server web
ssk macros run production-health --group production
ssk macros run production-health --tag production
ssk macros delete production-health --force
```

Execution reports the current target number, streamed output, exit code and final
outcome for each server. Each target runs the sequence in one strict POSIX shell,
preserving working directory and environment changes within that sequence and
stopping on command failure. Other selected servers are still attempted. A failed
target makes CLI execution return a failure exit code. Ctrl+C cancels the active
operation and stops further targets. Define needed initial directory/environment
inside the recorded sequence; shell state from before `:record` is not copied.

Commands that exit or replace the managed shell end its session. Shell functions
and stdout/stderr redirection are supported; descriptors 8 and 9 are reserved for
completion reporting and must not be closed. Output recordings and outcome events
use the policies in [Activity and recording](ACTIVITY_AND_RECORDING.md).
