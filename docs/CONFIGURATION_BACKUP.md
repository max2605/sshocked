# Configuration backup and restore

Open **Configuration backup and restore** to export, inspect, or restore a local
configuration snapshot. Backups are JSON documents using the
`sshocked-configuration` format, schema version `1`, a UTC creation timestamp, and
the application version. The file contains configured hosts, groups, macros,
target views, monitoring definitions, forwarding profiles, secret references, and
application policies. Runtime forwarding sessions are excluded; restoring profiles
does not open tunnels.

```text
ssk backup export configuration-2026-10-13.json
ssk backup inspect configuration-2026-10-13.json
ssk backup preview configuration-2026-10-13.json --mode merge
ssk backup restore configuration-2026-10-13.json --mode merge
ssk backup restore configuration-2026-10-13.json --mode merge --apply
```

`restore` without `--apply` shows a preview only. Both preview and restore require
an explicit `--mode merge` or `--mode replace`. The TUI shows the same changes and
requires confirmation before applying them. No remote commands are executed.

## Merge and replace

- **Merge** adds new records and updates records with matching stable IDs. Secret
  provider names are their case-insensitive identity. Existing records absent
  from the backup and current application settings remain. Monitoring definitions
  merge by ID; current monitoring runtime settings remain.
- **Replace** uses the entire backup configuration, including its application
  settings. Records absent from the backup are removed.

The preview lists added, changed, and removed records, plus changed settings.
Alias/name collisions across different IDs appear as conflicts and prevent
applying a merge. Review and correct the conflicting profiles, or explicitly
choose Replace and review its removals. Duplicate aliases/IDs within the input,
missing group/monitor targets, malformed JSON, invalid definitions, and unsupported
schema versions are rejected before applying.

The preview contains a configuration fingerprint. To ensure a later CLI restore
uses the configuration you reviewed, pass it explicitly:

```text
ssk backup restore configuration.json --mode replace --apply --expected FINGERPRINT
```

Every apply also compares the current configuration to its preview inside the
existing cross-process repository lock. A concurrent change invalidates the
preview; create a new preview instead of silently overwriting another edit.

## Recovery and atomic files

Before committing a restore, sshocked saves the exact previous persisted
configuration as a confidential local snapshot under the configuration directory's
private `recovery` subdirectory. The CLI prints its path. `--recovery NAME` selects
a filename within that directory; external destinations are rejected.
If the recovery file cannot be written, nothing is restored. If the configuration
commit fails, the recovery file remains and the repository's atomic file
replacement retains the previous configuration.

Local recovery uses its own `sshocked-local-recovery` format and is bound to the
current account and computer. Portable inspect/restore reject this format. Use
the explicit local recovery workflow:

```text
ssk backup recover /path/to/sshocked/recovery/recovery-file.json
ssk backup recover /path/to/sshocked/recovery/recovery-file.json --apply
```

The first command previews a full Replace recovery without applying it; `--apply`
is required after review. `--expected FINGERPRINT` can pin the reviewed current
configuration. The TUI offers **Preview local recovery**. Schema/account binding,
directory/file privacy and paths are checked before reading the snapshot. Only
direct files within the private recovery directory are accepted; links and reparse
points are rejected. Unix directory/files use 0700/0600; Windows ACLs grant access
only to the owning account. Keep these confidential snapshots local.

Backup/report writes
use a temporary file, flush it, and rename it into place; an existing destination
is never overwritten. Unix files are readable/writable only by their owner.
Files larger than 16 MiB are rejected.

## Portable data and credentials

Backups contain configuration data, not the entire application directory. They
never enumerate credential folders, include DPAPI ciphertext, read private-key
files, collect process environments, or copy audit/session-recording contents.
Transient SSH/sudo password fields are excluded by the configuration serializer.
Secret provider references remain; secret values are not resolved.

Hook environment dictionaries are removed because their values may contain
credentials. Recognized password/token literals, known transient host passwords,
and inline private-key material are redacted from configuration text. Hooks whose
environment or executable text was redacted are disabled for review. The backup
metadata reports how many values were removed or redacted. Review affected macros,
custom SSH options, and hooks before using them; arbitrary embedded secrets cannot
be identified reliably, so use secret references rather than inline values.

Local recovery preserves hook environments and inline configuration text exactly,
so it can roll back the prior settings completely. It still excludes transient
runtime passwords and never copies credential files or resolves secret values.
Portable export continues to sanitize the recovered configuration. Local passwords
remain separate and must be saved again for a different Windows account or SSH
endpoint; recovery does not transfer them or weaken endpoint binding.
