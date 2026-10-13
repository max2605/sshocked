# Command sessions and macros

## Create and edit without connecting

Open **Macros → Create macro** to define a command sequence without an SSH
connection. The editor offers **Set name**, **Add command**, **Edit command**,
**Delete command**, **Move command up/down**, and **View command sequence**.
Commands are numbered in their execution order. Selecting an existing command
opens its current text for editing; arrow keys, Home/End, Delete/Backspace, and
Ctrl+U edit the text. Esc cancels the text prompt. Enter keeps its current value.
An existing multiline command is shown in full; Enter preserves it exactly, or
you can enter a replacement command. Command whitespace and quotes are preserved.

Use **Browse saved macros → Edit macro** to change a saved definition, or
**Duplicate macro** to create an independent copy with an explicitly chosen new
name. Editing keeps the original ID, creation time, and recording provenance.
Duplication receives a new ID and creation time. Manually created macros have no
recording host.

Changes remain in a separate draft until **Save**. **Back** on a changed draft
offers **Save**, **Discard**, and **Back**; the last option returns to the editor.
Discard does not alter the saved macro. If another process changes or deletes a
macro while you edit it, saving reports a conflict and keeps your draft open.
Discard and reopen to load the latest version. Unrelated configuration changes
are retained by the repository's atomic update.

Names contain 1–80 characters without control characters and must be unique
without regard to letter case. A saved sequence contains 1–1000 commands, each
nonempty, at most 16384 characters, and without NUL. Secret detection follows the
same policy as recording. Creating, editing, duplicating, and saving never run
commands or open an SSH connection. Run the saved macro separately using the
existing server, group, or tag actions.

## Templates

**Macros → Create from template** copies a local template into an editable draft.
The initial **Ubuntu/Debian: update package index and upgrade packages** template
contains two separate steps:

```sh
sudo -n apt update
sudo -n apt upgrade -y
```

This template is specific to Ubuntu/Debian systems using APT. `update` refreshes
the package index before `upgrade` installs available package upgrades; it does
not upgrade the operating system to another release. See the
[Ubuntu package management tutorial](https://ubuntu.com/server/docs/tutorial/managing-software/).

The macro runner closes standard input, so `sudo -n` reports an error when a
password is required instead of opening a password prompt. Do not embed passwords,
pipe them into sudo, or assume permission exists on every target. The template
does not change sudoers. `-y` answers APT confirmation prompts but cannot guarantee
that every package script runs without further input. Use **Connect** for fully
interactive work. See the [sudo manual](https://manpages.debian.org/bookworm/sudo/sudo.8.en.html).
Selecting or saving a template does not execute updates. Editing a draft does not
modify the bundled template.

## Record commands

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

## Run saved macros

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
