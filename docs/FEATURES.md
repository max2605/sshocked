# SSH keys and OpenSSH configuration sync

## Select an SSH key

When adding or editing a server, select **SSH Key** authentication. The identity picker offers:

- **Pick from available keys**: choose a discovered private-key path with its algorithm, public-key SHA256 fingerprint, comment, and agent-loaded indicator.
- **Enter path manually**: use a key outside the discovered locations, including keys without a public-key companion.
- **Use SSH default keys**: leave `IdentityFile` empty and let OpenSSH select an identity.
- **Keep current key**: retain an existing host's identity.

Discovery scans `~/.ssh/id_*` private files with matching `.pub` files, plus existing `IdentityFile` references in `~/.ssh/config`. Quoted paths containing spaces and `~/` references are supported. Private-key contents are never read for discovery. Missing, malformed, or unreadable public keys are skipped; manual entry remains available.

Agent status comes from `ssh-add -L`. An unavailable command or agent does not prevent key selection. Encrypted keys still require a passphrase during the SSH connection or an identity loaded in an agent. The picker does not store passphrases.

## Import, export, and monitor SSH configuration

Open **SSH Config Sync** from the main menu. The default file is `~/.ssh/config` on all supported platforms.

### Import from OpenSSH config

The import action previews the concrete aliases it found and asks before applying changes. It reads `HostName`, `User`, `Port`, `IdentityFile`, `ProxyJump`, and additional directives as custom `-o` options. Quoted values, multiple concrete aliases on one `Host` line, wildcard defaults, negated patterns, and bounded recursive `Include` files are supported. OpenSSH's first obtained value wins when several matching blocks define a directive. An invalid port, unterminated quote, or Include cycle aborts the import.

Wildcard-only `Host` blocks provide defaults; they are not turned into connectable servers. Conditional `Match` directives are not evaluated by the importer. They remain untouched in the native configuration.

Existing hosts match by alias, then an explicit `# sshocked-id:` marker, then a unique hostname/user combination. A changed endpoint match participates in conflict resolution. Ambiguous endpoint matches become separate aliases rather than overwriting an arbitrary host.

Conflict policies are:

- **Ask for each conflict**: compare both versions, choose an action, and optionally reuse the choice for the remaining conflicts.
- **Keep sshocked version**: keep the configured host's connection fields.
- **Use OpenSSH version**: replace connection fields while retaining the host ID, group, tags, saved containers, and sshocked-specific settings.
- **Keep both**: add a distinct `-openssh` alias, using a numeric suffix if needed. Reimporting the same conflict does not create repeated copies.
- **Skip conflicting host**: leave it for a later decision.

OpenSSH does not represent sshocked's password, agent, or secret-provider settings. Import retains an existing authentication choice unless an imported identity requires key authentication. Removing a native host entry does not delete a sshocked host.

### Export to OpenSSH config

Export confirms the destination, validates all host values, and backs up each existing file that will change. Repeating an unchanged export does not rewrite files or create extra backups.

The default **MarkedBlocks** strategy owns only this section:

```sshconfig
# SSHOCKED_BEGIN: auto-generated, do not edit manually
# sshocked-id: stable-host-id
Host web
    HostName "web.example.com"
    User "ubuntu"
    Port "22"
    IdentityFile "~/.ssh/id_ed25519"

Host *
# SSHOCKED_END
```

The section is inserted after existing global directives and before the first `Host` or `Match` block. The final empty `Host *` restores default scope for following user content. Comments and directives outside the marked section remain intact. Duplicate, missing, or reversed markers abort export instead of guessing which content to replace.

The alternative **IncludeFile** strategy writes a dedicated file and adds an `Include` directive in the main file's global preamble. Its default destination is `~/.config/sshocked/ssh-config-include`. A configured include destination containing unmanaged content is rejected. The main file and include destination must differ.

Backups use a `.sshocked-<timestamp>-<suffix>.bak` suffix next to their original files. Writes use temporary files and atomic replacement. Existing Unix file modes are retained; newly created files are private to the user. If a file changes while an export is being prepared, that write is rejected so it can be reviewed and retried.

Export refuses symbolic-link main/include destinations, including broken links, before changing generated files or backups. Choose the physical file path to preserve the existing link.

Export writes alias, hostname, optional user/port/identity, ProxyJump, and custom SSH options. Custom options expressed as `-o Keyword=value` are mapped to directives; `-J`, `-i`, `-p`, and `-l` also map to directives. Other CLI-only flags cannot be exported and produce an explanatory error before the export is applied. `Host`, `Match`, and `Include` cannot be injected through custom options. ProxyCommand and other command directives retain their command quoting.

### Configure automatic sync

Automatic import and export are disabled initially. **Configure sync settings** lets you choose the export strategy/path, tags, excluded aliases, fields, startup import, automatic behavior, and conflict policy. **Enable auto-sync** turns on both automatic import and export; the same action disables both when already enabled.

Auto-export follows persisted host changes. Auto-import watches the main config and the configured managed include file, including create/delete events and atomic editor renames. A 500 ms debounce combines short bursts of writes. Export-generated notifications and repository saves made by import are suppressed to prevent loops.

With **Ask for each conflict**, background import adds unambiguous new hosts and defers conflicting hosts. It never opens a terminal prompt from a watcher thread. Open the sync menu and use manual import to resolve pending conflicts. Errors are logged and appear in **View sync status**, along with last import/export times and counts, watcher state, configured/native alias counts, and pending differences.

Only the main config and the selected managed include file are monitored automatically. Other Include files are read during import, but changing those files alone does not trigger monitoring. System-wide SSH configuration, dynamic `Match` evaluation, field-by-field conflict merges, private-key generation, and destructive whole-file overwrite are not provided by this workflow.
