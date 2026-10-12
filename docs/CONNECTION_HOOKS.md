# Connection hooks

Configure hooks in the host editor or the global hooks menu. Global hooks can include
hosts with any matching tag and exclude hosts with any matching tag; exclusions win.
Matching is case insensitive. Disabled hooks are skipped.

The execution order is global then host for pre-connect and pre-disconnect, and host
then global for post-connect and post-disconnect.

## Timing and remote shell state

Local pre-connect hooks run before SSH starts. A blocking failure stops the connection
unless `continueOnError` is enabled. Local post-connect hooks run when the SSH child
starts; the inherited terminal does not expose an authentication-complete event.
Local pre-disconnect hooks run when the child exits, followed by local post-disconnect
hooks. A local command cannot reliably run immediately before a user exits an
interactive SSH process.

Remote post-connect commands run after authentication in the same shell wrapper that
starts the interactive shell. A blocking `cd` or exported environment variable is
therefore inherited by the interactive shell. Use shell syntax supported by the
server's login shell; for portable environment setup, use `. /path/to/environment`.
Remote pre-disconnect commands run after the interactive child shell exits while the
SSH connection remains open. A lost network connection or a killed SSH process can
prevent remote cleanup. Pre-connect and post-disconnect hooks are always local.

`timeoutSeconds` applies to local hooks and kills their process tree on expiry.
Remote commands execute in the current shell to preserve directory and environment
changes, so this implementation does not enforce per-hook remote timeouts. Configure
remote command timeouts inside the command when appropriate; doing so in a subshell
will not propagate directory or environment changes to the interactive shell.

Nonblocking hooks run in the background. Their failures are audited but cannot abort
an already started connection. Background remote hooks cannot change the interactive
shell's state. Local background hooks are canceled and awaited when the application
shuts down so child processes and temporary inline scripts are cleaned up.

## Scripts, output and metadata

Local commands and inline scripts use PowerShell on Windows and `/bin/sh` on Unix.
Local script files support `.ps1` (PowerShell), `.sh` (shell), `.py` (Python), and native
executables. Their interpreters must be installed. Local script arguments are split
into literal process arguments; `${SSHOCKED_HOST_ALIAS}` and other metadata references
in those arguments are expanded without evaluating them as shell syntax. Shell
commands use their shell's environment syntax, such as `$env:SSHOCKED_HOST_ALIAS` on
Windows or `$SSHOCKED_HOST_ALIAS` on Unix.

All hooks receive `SSHOCKED_HOOK_TYPE`, `SSHOCKED_HOST_ID`, `SSHOCKED_HOST_ALIAS`,
`SSHOCKED_HOST_NAME`, `SSHOCKED_HOST_PORT`, `SSHOCKED_HOST_USER`, `SSHOCKED_HOST_GROUP`,
`SSHOCKED_HOST_TAGS`, `SSHOCKED_CONNECTION_TIMESTAMP`, and `SSHOCKED_CONFIG_DIR`.
Local disconnect hooks also receive the final exit code and session duration when
available. Remote pre-disconnect hooks receive the interactive shell's exit code.
SSH and sudo passwords are not included in this metadata.

Local output modes are log, console, both, and suppress. Remote output travels through
the SSH terminal; it is not captured into the local application log. Suppress redirects
remote command stdout and stderr to `/dev/null`. The audit trail distinguishes local
hook outcomes from remote scheduling events; a scheduling event does not establish
that a remote hook completed successfully.

## JSON example

Add this `hooks` property to an existing host in `appconfig.json`. The generated JSON
serializer stores enums numerically: remote command mode is `1`, and console output
mode is `1`.

```json
{
  "hooks": {
    "postConnect": {
      "mode": 1,
      "command": "cd /var/log && export APP_ENV=production",
      "blocking": true,
      "continueOnError": false,
      "enabled": true,
      "outputMode": 1
    },
    "preDisconnect": {
      "mode": 1,
      "command": "printf 'Session finished\\n'",
      "continueOnError": true,
      "outputMode": 1
    }
  }
}
```

Global configuration uses the root `globalHooks` property with the same lifecycle
properties and optional `includeTags` / `excludeTags` arrays.
